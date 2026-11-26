using System.Collections.Frozen;

using CardPlatform.Contracts;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// Processes one consumed message exactly once per event id (ADR 0011): reads the envelope, finds the handler of its
/// type and, in a new scope and one transaction of its <typeparamref name="TDbContext"/>, inserts the event id into
/// <c>processed_messages</c> (<c>INSERT ... ON CONFLICT DO NOTHING</c>) and, only if the row is new, runs the handler,
/// saves and commits. A copy of an event already processed, or being processed by another consumer at the same time (the
/// insert waits for that transaction and then finds the row), changes nothing. Independent of Kafka, which only feeds it.
/// </summary>
/// <remarks>
/// The context's database must be PostgreSQL and its model must apply
/// <see cref="ProcessedMessageModelBuilderExtensions.ApplyProcessedMessages"/>.
/// </remarks>
public sealed class IntegrationEventProcessor<TDbContext>
    where TDbContext : DbContext
{
    private const string InsertProcessedMessageSql =
        "INSERT INTO " + ProcessedMessageConfiguration.TableName
        + " (" + ProcessedMessageConfiguration.EventIdColumn
        + ", " + ProcessedMessageConfiguration.TypeColumn
        + ", " + ProcessedMessageConfiguration.ProcessedAtColumn
        + ") VALUES ({0}, {1}, {2}) ON CONFLICT (" + ProcessedMessageConfiguration.EventIdColumn + ") DO NOTHING";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IntegrationEventProcessor<TDbContext>> _logger;
    private readonly FrozenDictionary<string, IntegrationEventHandlerRegistration> _handlers;

    public IntegrationEventProcessor(
        IServiceScopeFactory scopeFactory,
        IEnumerable<IntegrationEventHandlerRegistration> registrations,
        TimeProvider timeProvider,
        ILogger<IntegrationEventProcessor<TDbContext>> logger)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;

        Dictionary<string, IntegrationEventHandlerRegistration> handlers = new(StringComparer.Ordinal);
        foreach (IntegrationEventHandlerRegistration registration in registrations)
        {
            if (!handlers.TryAdd(registration.EventType.Name, registration))
            {
                throw new InvalidOperationException(
                    $"More than one handler is registered for the integration event '{registration.EventType.Name}'.");
            }
        }

        _handlers = handlers.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Processes the message whose value is <paramref name="value"/> (an envelope written by <see cref="EventSerializer"/>).</summary>
    /// <exception cref="System.Text.Json.JsonException">The value is not an envelope, or its payload does not match its type.</exception>
    /// <exception cref="Exception">The handler or the database failed; nothing was committed.</exception>
    public async Task<EventProcessingResult> ProcessAsync(ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
    {
        EventEnvelope envelope = EventSerializer.Deserialize(value.Span);

        if (!_handlers.TryGetValue(envelope.Type, out IntegrationEventHandlerRegistration? handler))
        {
            if (EventTypes.TryFind(envelope.Type, out _))
            {
                ConsumerLog.EventNotHandled(_logger, envelope.EventId, envelope.Type);
            }
            else
            {
                ConsumerLog.UnknownEventType(_logger, envelope.EventId, envelope.Type, envelope.Version);
            }

            return EventProcessingResult.Skipped;
        }

        IIntegrationEvent payload = EventSerializer.DeserializePayload(envelope);

        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            // The context may use a retrying execution strategy (the Aspire default), which requires running a
            // user-initiated transaction through it. A retried attempt starts over from what is committed; if the first
            // attempt did commit, the retry finds the processed row and does nothing.
            IExecutionStrategy strategy = dbContext.Database.CreateExecutionStrategy();

            return await strategy.ExecuteAsync(
                (Services: scope.ServiceProvider, DbContext: dbContext),
                (_, state, token) => ProcessInTransactionAsync(state.Services, state.DbContext, handler, payload, envelope, token),
                verifySucceeded: null,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<EventProcessingResult> ProcessInTransactionAsync(
        IServiceProvider services,
        TDbContext dbContext,
        IntegrationEventHandlerRegistration handler,
        IIntegrationEvent payload,
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();

        IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            // Marked first: the row lock makes a concurrent copy of the event (e.g. redelivered to another consumer after
            // a rebalance) wait here until this transaction ends, and then find the row.
            int inserted = await dbContext.Database.ExecuteSqlRawAsync(
                InsertProcessedMessageSql,
                [envelope.EventId, envelope.Type, TruncateToMicroseconds(_timeProvider.GetUtcNow())],
                cancellationToken).ConfigureAwait(false);

            if (inserted == 0)
            {
                ConsumerLog.DuplicateEvent(_logger, envelope.EventId, envelope.Type);
                return EventProcessingResult.Duplicate;
            }

            await handler.InvokeAsync(services, payload, envelope, cancellationToken).ConfigureAwait(false);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return EventProcessingResult.Processed;
        }
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        DateTimeOffset utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
