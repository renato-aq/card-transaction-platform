using System.Globalization;
using System.Text;
using System.Text.Json;

using CardPlatform.Contracts;
using CardPlatform.Messaging.Kafka;

using Confluent.Kafka;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// Background service that consumes integration events from Kafka (ADR 0011): it subscribes to
/// <see cref="IntegrationEventConsumerOptions.Topics"/> with the consumer registered by the Aspire integration and, for
/// each message, lets <see cref="IntegrationEventProcessor{TDbContext}"/> process it once per event id, then commits the
/// message's offset, only after the database transaction committed. Delivery is therefore at least once and processing
/// exactly once: a message whose processing committed but whose offset did not is delivered again and skipped.
/// </summary>
/// <remarks>
/// A message that fails is not committed. A temporary failure (a handler error, the database down) rewinds its partition
/// to it and retries it after a bounded exponential backoff, before any later message of that partition, up to
/// <see cref="IntegrationEventConsumerOptions.MaxAttempts"/> attempts, counted per message. A permanent failure (a
/// message that is not a valid envelope, <see cref="IntegrationEventRejectedException"/>), or a temporary one with no
/// attempt left, sends the message to its dead-letter topic (<c>&lt;topic&gt;.dlt</c>) with
/// <see cref="KafkaDeadLetterProducer"/> and, once the broker acknowledged it, commits its offset, so the partition
/// moves on in order. If that send fails, nothing is committed and the send is retried after a backoff: a message is
/// never lost.
/// </remarks>
public sealed class IntegrationEventConsumer<TDbContext>(
    IConsumer<string, byte[]> consumer,
    IntegrationEventProcessor<TDbContext> processor,
    KafkaDeadLetterProducer deadLetterProducer,
    IOptions<IntegrationEventConsumerOptions> options,
    TimeProvider timeProvider,
    IHostApplicationLifetime lifetime,
    ILogger<IntegrationEventConsumer<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private readonly IntegrationEventConsumerOptions _options = options.Value;
    private readonly FailedMessageTracker _failures = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Consume blocks its thread; leave the host's startup path before the first call.
        await Task.Yield();

        consumer.Subscribe(_options.Topics);
        string topics = string.Join(", ", _options.Topics);
        ConsumerLog.Subscribed(logger, topics);

        try
        {
            await ConsumeAsync(stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            // Leaves the group at once, so its partitions are reassigned without waiting for the session timeout.
            consumer.Close();
        }
    }

    /// <summary>A message is permanently unprocessable when it is not a valid envelope or its handler rejected it.</summary>
    internal static bool IsPermanent(Exception exception)
    {
        return exception is JsonException or IntegrationEventRejectedException;
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        int consumeFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, byte[]>? result;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (KafkaException exception) when (exception.Error.IsFatal)
            {
                ConsumerLog.FatalConsumerError(logger, exception);
                lifetime.StopApplication();
                return;
            }
            catch (ConsumeException exception)
            {
                // E.g. a subscribed topic does not exist (yet).
                consumeFailures++;
                TimeSpan delay = RetryDelay(consumeFailures);
                ConsumerLog.ConsumeFailed(logger, exception, consumeFailures, delay);
                if (!await DelayAsync(delay, stoppingToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            if (consumeFailures > 0)
            {
                ConsumerLog.Resumed(logger, consumeFailures);
                consumeFailures = 0;
            }

            if (result?.Message is null)
            {
                continue;
            }

            if (!await HandleAsync(result, stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <summary>Processes, retries or dead-letters one message; returns <see langword="false"/> when the loop must stop.</summary>
    private async Task<bool> HandleAsync(ConsumeResult<string, byte[]> result, CancellationToken stoppingToken)
    {
        if (_failures.PendingDeadLetterOf(result.TopicPartitionOffset) is { } pending)
        {
            // Its dead-lettering was decided before but the send failed: send it again, without processing it again.
            return await DeadLetterAsync(result, pending.Exception, pending.Attempts, pending.Permanent, stoppingToken)
                .ConfigureAwait(false);
        }

        EventProcessingResult processed;
        try
        {
            processed = await processor.ProcessAsync(result.Message.Value, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping: nothing was committed, so the message is delivered again.
            return false;
        }
#pragma warning disable CA1031 // Any failure is retried or dead-lettered; the consumer never stops on a message.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            int attempts = _failures.RecordFailure(result.TopicPartitionOffset);
            bool permanent = IsPermanent(exception);
            if (permanent || attempts >= _options.MaxAttempts)
            {
                return await DeadLetterAsync(result, exception, attempts, permanent, stoppingToken).ConfigureAwait(false);
            }

            TimeSpan delay = RetryDelay(attempts);
            ConsumerLog.ProcessingFailed(
                logger,
                exception,
                result.Topic,
                result.Partition.Value,
                result.Offset.Value,
                attempts,
                _options.MaxAttempts,
                delay);

            Rewind(result);
            return await DelayAsync(delay, stoppingToken).ConfigureAwait(false);
        }

        int failedAttempts = _failures.Clear(result.TopicPartitionOffset);
        if (failedAttempts > 0)
        {
            ConsumerLog.Recovered(logger, result.Topic, result.Partition.Value, result.Offset.Value, failedAttempts);
        }

        ConsumerLog.MessageConsumed(logger, result.Topic, result.Partition.Value, result.Offset.Value, processed);

        return Commit(result);
    }

    /// <summary>
    /// Sends the message to its dead-letter topic and commits its offset once the broker acknowledged it; if the send
    /// fails, leaves it uncommitted and rewinds to it, so it is sent again after a backoff. Returns
    /// <see langword="false"/> when the loop must stop.
    /// </summary>
    private async Task<bool> DeadLetterAsync(
        ConsumeResult<string, byte[]> result,
        Exception exception,
        int attempts,
        bool permanent,
        CancellationToken stoppingToken)
    {
        string deadLetterTopic;
        try
        {
            deadLetterTopic = await deadLetterProducer.ProduceAsync(result, exception, attempts, permanent, stoppingToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (KafkaException sendException)
        {
            int sendFailures = _failures.RecordDeadLetterFailure(result.TopicPartitionOffset, exception, attempts, permanent);
            TimeSpan delay = RetryDelay(sendFailures);
            ConsumerLog.DeadLetterFailed(
                logger,
                sendException,
                result.Topic,
                result.Partition.Value,
                result.Offset.Value,
                Topics.DeadLetterOf(result.Topic),
                delay);

            Rewind(result);
            return await DelayAsync(delay, stoppingToken).ConfigureAwait(false);
        }

        _failures.Clear(result.TopicPartitionOffset);
        (Guid? eventId, string? eventType) = Identify(result.Message);
        ConsumerLog.DeadLettered(
            logger,
            exception,
            result.Topic,
            result.Partition.Value,
            result.Offset.Value,
            eventId,
            eventType,
            deadLetterTopic,
            attempts,
            permanent ? DeadLetterHeaders.PermanentFailure : DeadLetterHeaders.RetriesExhausted,
            exception.Message);

        return Commit(result);
    }

    /// <summary>The event id and type of a message, for logs: from its headers, or else from its envelope if it has one.</summary>
    private static (Guid? EventId, string? EventType) Identify(Message<string, byte[]> message)
    {
        Guid? eventId = null;
        string? eventType = null;

        if (message.Headers is { } headers)
        {
            if (headers.TryGetLastBytes(OutboxHeaders.EventId, out byte[] idBytes)
                && Guid.TryParse(Encoding.UTF8.GetString(idBytes), CultureInfo.InvariantCulture, out Guid id))
            {
                eventId = id;
            }

            if (headers.TryGetLastBytes(OutboxHeaders.EventType, out byte[] typeBytes))
            {
                eventType = Encoding.UTF8.GetString(typeBytes);
            }
        }

        if ((eventId is null || eventType is null) && message.Value is { Length: > 0 } value)
        {
            try
            {
                EventEnvelope envelope = EventSerializer.Deserialize(value);
                eventId ??= envelope.EventId;
                eventType ??= envelope.Type;
            }
            catch (JsonException)
            {
                // Not an envelope: nothing more to tell.
            }
        }

        return (eventId, eventType);
    }

    /// <summary>Commits the offset after <paramref name="result"/>; returns <see langword="false"/> on a fatal error.</summary>
    private bool Commit(ConsumeResult<string, byte[]> result)
    {
        try
        {
            consumer.Commit(result);
            return true;
        }
        catch (KafkaException exception) when (exception.Error.IsFatal)
        {
            ConsumerLog.FatalConsumerError(logger, exception);
            lifetime.StopApplication();
            return false;
        }
        catch (KafkaException exception)
        {
            // E.g. the partition was revoked by a rebalance; the processed mark makes the redelivery harmless.
            ConsumerLog.CommitFailed(logger, exception, result.Topic, result.Partition.Value, result.Offset.Value);
            return true;
        }
    }

    /// <summary>Makes the next fetch of the message's partition start at the message again.</summary>
    private void Rewind(ConsumeResult<string, byte[]> result)
    {
        try
        {
            consumer.Seek(result.TopicPartitionOffset);
        }
        catch (KafkaException exception)
        {
            ConsumerLog.SeekFailed(logger, exception, result.Topic, result.Partition.Value, result.Offset.Value);
        }
    }

    private TimeSpan RetryDelay(int failures)
    {
        return ExponentialBackoff.For(failures, _options.RetryInitialDelay, _options.RetryMaxDelay);
    }

    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
