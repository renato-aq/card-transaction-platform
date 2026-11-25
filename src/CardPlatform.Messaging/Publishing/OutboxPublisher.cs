using Confluent.Kafka;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardPlatform.Messaging.Publishing;

/// <summary>
/// Background service that keeps publishing the outbox of <typeparamref name="TDbContext"/> to Kafka (ADR 0010): it
/// runs <see cref="OutboxBatchPublisher{TDbContext}"/> cycles back to back while full batches succeed, waits
/// <see cref="OutboxPublisherOptions.PollingInterval"/> when the outbox is drained or another instance is publishing,
/// and backs off exponentially (bounded) after failures, without ever stopping on them: when the broker or the
/// database comes back, the next cycle publishes what waited.
/// </summary>
public sealed class OutboxPublisher<TDbContext>(
    OutboxBatchPublisher<TDbContext> batchPublisher,
    IOptions<OutboxPublisherOptions> options,
    TimeProvider timeProvider,
    IHostApplicationLifetime lifetime,
    ILogger<OutboxPublisher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private readonly OutboxPublisherOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string dbContextName = typeof(TDbContext).Name;
        int consecutiveFailures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                OutboxBatchResult result = await batchPublisher.PublishPendingAsync(stoppingToken).ConfigureAwait(false);
                if (result.Published > 0)
                {
                    OutboxLog.Published(logger, result.Published, dbContextName);
                }

                if (result.Failure is null)
                {
                    if (consecutiveFailures > 0)
                    {
                        OutboxLog.Resumed(logger, dbContextName, consecutiveFailures);
                    }

                    consecutiveFailures = 0;
                    delay = result.MayHaveMore ? TimeSpan.Zero : _options.PollingInterval;
                }
                else
                {
                    if (result.Failure is KafkaException { Error.IsFatal: true })
                    {
                        // A fatal error leaves the (singleton) producer unusable; a restart creates a new one.
                        OutboxLog.FatalProducerError(logger, result.Failure);
                        lifetime.StopApplication();
                        return;
                    }

                    consecutiveFailures++;
                    delay = OutboxRetryDelay.For(consecutiveFailures, _options);
                    OutboxLog.CycleFailed(logger, result.Failure, dbContextName, consecutiveFailures, delay);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The publisher must survive any failure (e.g. the database is down) and retry.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                consecutiveFailures++;
                delay = OutboxRetryDelay.For(consecutiveFailures, _options);
                OutboxLog.CycleFailed(logger, exception, dbContextName, consecutiveFailures, delay);
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }
}
