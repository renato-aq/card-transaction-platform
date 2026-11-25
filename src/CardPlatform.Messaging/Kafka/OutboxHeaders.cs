namespace CardPlatform.Messaging.Kafka;

/// <summary>
/// Kafka headers set on every published event, so consumers can route or deduplicate without parsing the value. The
/// value is the whole <see cref="Contracts.EventEnvelope"/>, which carries the same data.
/// </summary>
public static class OutboxHeaders
{
    /// <summary>The <see cref="Contracts.EventEnvelope.EventId"/> (UUID, <c>D</c> format, UTF-8).</summary>
    public const string EventId = "event-id";

    /// <summary>The <see cref="Contracts.EventEnvelope.Type"/> (UTF-8).</summary>
    public const string EventType = "event-type";
}
