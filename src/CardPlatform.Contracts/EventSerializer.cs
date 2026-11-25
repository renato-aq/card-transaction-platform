using System.Text.Json;

namespace CardPlatform.Contracts;

/// <summary>
/// The single place that defines how integration events are written to and read from JSON (camelCase), so producers
/// and consumers always agree on the format.
/// </summary>
public static class EventSerializer
{
    /// <summary>
    /// Options for every integration event: camelCase names, and strict reading (required properties must be
    /// present and non-nullable ones cannot be <c>null</c>), so a malformed message fails instead of half-loading.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return JsonSerializer.Serialize(envelope, Options);
    }

    public static byte[] SerializeToUtf8Bytes(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
    }

    /// <exception cref="JsonException">The JSON is not a valid envelope.</exception>
    public static EventEnvelope Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        return JsonSerializer.Deserialize<EventEnvelope>(json, Options) ?? throw NullEnvelope();
    }

    /// <exception cref="JsonException">The JSON is not a valid envelope.</exception>
    public static EventEnvelope Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        return JsonSerializer.Deserialize<EventEnvelope>(utf8Json, Options) ?? throw NullEnvelope();
    }

    /// <summary>Reads the payload as the event type named by <see cref="EventEnvelope.Type"/>.</summary>
    /// <exception cref="JsonException">The type is unknown or the payload does not match it.</exception>
    public static IIntegrationEvent DeserializePayload(EventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!EventTypes.TryFind(envelope.Type, out EventType? eventType))
        {
            throw new JsonException($"Unknown integration event type '{envelope.Type}'.");
        }

        return (IIntegrationEvent?)envelope.Payload.Deserialize(eventType.ClrType, Options)
            ?? throw new JsonException($"The payload of event {envelope.EventId} is null.");
    }

    /// <summary>Reads the payload as <typeparamref name="TEvent"/>, which must be the type the envelope names.</summary>
    /// <exception cref="JsonException">The envelope carries another event type or the payload does not match it.</exception>
    public static TEvent DeserializePayload<TEvent>(EventEnvelope envelope)
        where TEvent : IIntegrationEvent
    {
        IIntegrationEvent payload = DeserializePayload(envelope);

        return payload is TEvent typed
            ? typed
            : throw new JsonException(
                $"Event {envelope.EventId} is '{envelope.Type}', not {typeof(TEvent).Name}.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.General)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }

    private static JsonException NullEnvelope()
    {
        return new JsonException("The message is not an event envelope (null).");
    }
}
