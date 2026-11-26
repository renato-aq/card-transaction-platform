namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// A row of <c>processed_messages</c>: an integration event that a consumer of the service already processed (ADR 0011).
/// It is written in the same database transaction as what processing the event changed, so an event is either
/// processed and marked, or neither; a copy of an event whose id is here is skipped. Shared by every service that
/// consumes events, as <see cref="Outbox.OutboxMessage"/> is by those that publish them.
/// </summary>
/// <remarks>
/// Keyed by event id alone: a service database holds the processed events of one consumer (one consumer group). Event
/// ids are unique across event types and topics, so one consumer may read several topics.
/// </remarks>
public sealed class ProcessedMessage
{
    // Rows are inserted by IntegrationEventProcessor with INSERT ... ON CONFLICT DO NOTHING; EF Core only reads them.
    private ProcessedMessage(Guid eventId, string type, DateTimeOffset processedAt)
    {
        EventId = eventId;
        Type = type;
        ProcessedAt = processedAt;
    }

    /// <summary>The <see cref="Contracts.EventEnvelope.EventId"/> of the event.</summary>
    public Guid EventId { get; private set; }

    /// <summary>The <see cref="Contracts.EventEnvelope.Type"/> of the event, for diagnostics.</summary>
    public string Type { get; private set; }

    /// <summary>When the event was processed (UTC, whole microseconds).</summary>
    public DateTimeOffset ProcessedAt { get; private set; }
}
