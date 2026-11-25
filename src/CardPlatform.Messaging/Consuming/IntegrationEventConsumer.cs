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
/// A message that fails (malformed, a handler error, the database down) is not committed: the consumer rewinds its
/// partition to it and retries it after a bounded exponential backoff, without ever skipping it, so a message is never
/// lost and the events of a card keep their order. Bounded retries and a dead-letter topic are a later change; this is
/// where they will plug in.
/// </remarks>
public sealed class IntegrationEventConsumer<TDbContext>(
    IConsumer<string, byte[]> consumer,
    IntegrationEventProcessor<TDbContext> processor,
    IOptions<IntegrationEventConsumerOptions> options,
    TimeProvider timeProvider,
    IHostApplicationLifetime lifetime,
    ILogger<IntegrationEventConsumer<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private readonly IntegrationEventConsumerOptions _options = options.Value;

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

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        int consecutiveFailures = 0;

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
                consecutiveFailures++;
                TimeSpan delay = RetryDelay(consecutiveFailures);
                ConsumerLog.ConsumeFailed(logger, exception, consecutiveFailures, delay);
                if (!await DelayAsync(delay, stoppingToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            if (result?.Message is null)
            {
                continue;
            }

            EventProcessingResult processed;
            try
            {
                processed = await processor.ProcessAsync(result.Message.Value, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Stopping: nothing was committed, so the message is delivered again.
                return;
            }
#pragma warning disable CA1031 // Any failure leaves the message uncommitted and retries it; the consumer never stops on it.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                consecutiveFailures++;
                TimeSpan delay = RetryDelay(consecutiveFailures);
                ConsumerLog.ProcessingFailed(
                    logger,
                    exception,
                    result.Topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    consecutiveFailures,
                    delay);

                Rewind(result);
                if (!await DelayAsync(delay, stoppingToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            if (consecutiveFailures > 0)
            {
                ConsumerLog.Resumed(logger, consecutiveFailures);
                consecutiveFailures = 0;
            }

            ConsumerLog.MessageConsumed(logger, result.Topic, result.Partition.Value, result.Offset.Value, processed);

            if (!Commit(result))
            {
                return;
            }
        }
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

    private TimeSpan RetryDelay(int consecutiveFailures)
    {
        return ExponentialBackoff.For(consecutiveFailures, _options.RetryInitialDelay, _options.RetryMaxDelay);
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
