using System.Globalization;
using System.Text.Json;

namespace CardPlatform.Contracts;

/// <summary>
/// The message published to Kafka: metadata common to every event plus the serialized <see cref="Payload"/>.
/// </summary>
/// <param name="EventId">Unique id of the event; consumers use it to process each event once (ADR 0003).</param>
/// <param name="Type">Wire name of the event (see <see cref="EventTypes"/>); tells which payload type to read.</param>
/// <param name="Version">Version of the event contract the payload was written with.</param>
/// <param name="OccurredAt">When the event happened.</param>
/// <param name="PartitionKey">Kafka message key: the card id, so the events of a card stay ordered.</param>
/// <param name="Payload">The event itself, read with <see cref="EventSerializer.DeserializePayload(EventEnvelope)"/>.</param>
public sealed record EventEnvelope(
    Guid EventId,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    string PartitionKey,
    JsonElement Payload)
{
    /// <summary>
    /// Wraps <paramref name="payload"/>, taking its type name and version from <see cref="EventTypes"/> and its
    /// partition key from the card id.
    /// </summary>
    public static EventEnvelope Create(IIntegrationEvent payload, Guid eventId, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(payload);

        EventType eventType = EventTypes.Of(payload.GetType());

        return new EventEnvelope(
            eventId,
            eventType.Name,
            eventType.Version,
            occurredAt,
            payload.CardId.ToString("D", CultureInfo.InvariantCulture),
            JsonSerializer.SerializeToElement(payload, eventType.ClrType, EventSerializer.Options));
    }
}
