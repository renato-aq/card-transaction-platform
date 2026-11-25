using CardPlatform.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardPlatform.Messaging.Publishing;

/// <summary>
/// One publishing cycle over the outbox of <typeparamref name="TDbContext"/> (ADR 0010). In one database transaction it
/// takes a PostgreSQL advisory lock (<c>pg_try_advisory_xact_lock</c>), so only one instance publishes at a time,
/// reads up to <see cref="OutboxPublisherOptions.BatchSize"/> unpublished rows in id order and sends them one by one,
/// waiting for the broker's acknowledgement of each before sending the next, so the rows of a partition key reach
/// Kafka in the order they were read. A row is marked as published only after its acknowledgement; the first failure
/// stops the cycle, so no later row (of that key or any other) overtakes the one that failed.
/// </summary>
public sealed class OutboxBatchPublisher<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IOutboxMessageProducer producer,
    IOptions<OutboxPublisherOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxBatchPublisher<TDbContext>> logger)
    where TDbContext : DbContext
{
    private readonly OutboxPublisherOptions _options = options.Value;

    /// <summary>
    /// Publishes the next batch of unpublished rows, or does nothing when another instance holds the lock.
    /// </summary>
    public async Task<OutboxBatchResult> PublishPendingAsync(CancellationToken cancellationToken)
    {
        AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            // The context may use a retrying execution strategy (the Aspire default), which requires running a
            // user-initiated transaction through it. A retried cycle starts over from what is committed.
            IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(
                dbContext,
                (context, token) => PublishInTransactionAsync(context, token),
                verifySucceeded: null,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<OutboxBatchResult> PublishInTransactionAsync(TDbContext dbContext, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();

        IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            if (!await TryLockAsync(dbContext, cancellationToken).ConfigureAwait(false))
            {
                OutboxLog.LockHeldElsewhere(logger, typeof(TDbContext).Name);
                return OutboxBatchResult.LockNotAcquired;
            }

            List<OutboxMessage> batch = await dbContext.Set<OutboxMessage>()
                .Where(static message => message.PublishedAt == null)
                .OrderBy(static message => message.Id)
                .Take(_options.BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            (int published, Exception? failure) = await PublishAsync(batch, cancellationToken).ConfigureAwait(false);

            // Saved even when the host is stopping, so the acknowledged rows are not published again.
            await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return new OutboxBatchResult(
                Skipped: false,
                published,
                failure,
                MayHaveMore: failure is null && batch.Count == _options.BatchSize);
        }
    }

    private async Task<(int Published, Exception? Failure)> PublishAsync(
        List<OutboxMessage> batch,
        CancellationToken cancellationToken)
    {
        int published = 0;
        foreach (OutboxMessage message in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            message.RecordAttempt();
            try
            {
                await producer.ProduceAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Stopping: the row stays unpublished (it may still reach the broker and be published again later).
                break;
            }
#pragma warning disable CA1031 // Any failure stops the cycle; the row is retried later with backoff.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                OutboxLog.PublishFailed(logger, exception, message.Id, message.EventId, message.Topic, message.Attempts);
                return (published, exception);
            }

            message.MarkPublished(timeProvider.GetUtcNow());
            published++;
        }

        return (published, null);
    }

    private Task<bool> TryLockAsync(TDbContext dbContext, CancellationToken cancellationToken)
    {
        // Released by PostgreSQL when the transaction ends, including when the connection is lost.
        return dbContext.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({_options.LockKey}) AS \"Value\"")
            .SingleAsync(cancellationToken);
    }
}
