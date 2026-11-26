using System.Text.Json;

using CardPlatform.Contracts.Authorizations;
using CardPlatform.Contracts.Fraud;

namespace CardPlatform.Contracts.Tests;

public class EventSerializerTests
{
    [Theory]
    [MemberData(nameof(SampleEvents.AllWithTypeNames), MemberType = typeof(SampleEvents))]
    public void Envelope_type_identifies_the_event_and_reads_the_right_payload(IIntegrationEvent @event, string typeName)
    {
        string json = EventSerializer.Serialize(EventEnvelope.Create(@event, Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        EventEnvelope envelope = EventSerializer.Deserialize(json);
        IIntegrationEvent payload = EventSerializer.DeserializePayload(envelope);

        Assert.Equal(typeName, envelope.Type);
        Assert.IsType(@event.GetType(), payload);
    }

    [Fact]
    public void Envelope_and_payload_are_written_in_camel_case()
    {
        EventEnvelope envelope = EventEnvelope.Create(SampleEvents.Declined, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        using JsonDocument json = JsonDocument.Parse(EventSerializer.Serialize(envelope));
        JsonElement root = json.RootElement;

        string[] envelopeProperties = [.. root.EnumerateObject().Select(property => property.Name)];
        Assert.Equal(["eventId", "type", "version", "occurredAt", "partitionKey", "payload"], envelopeProperties);

        JsonElement payload = root.GetProperty("payload");
        string[] payloadProperties = [.. payload.EnumerateObject().Select(property => property.Name)];
        Assert.Equal(
            ["authorizationId", "cardId", "merchantAccountId", "amount", "currency", "createdAt", "reason"],
            payloadProperties);
        Assert.Equal("insufficient_funds", payload.GetProperty("reason").GetString());
        Assert.Equal("authorization.declined", root.GetProperty("type").GetString());
    }

    [Fact]
    public void Partition_key_is_the_card_id()
    {
        EventEnvelope envelope = EventEnvelope.Create(SampleEvents.FraudSuspected, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Equal(SampleEvents.FraudSuspected.CardId.ToString(), envelope.PartitionKey);
    }

    [Fact]
    public void Typed_payload_read_returns_the_event()
    {
        EventEnvelope envelope = EventEnvelope.Create(SampleEvents.FraudSuspected, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        FraudSuspected payload = EventSerializer.DeserializePayload<FraudSuspected>(envelope);

        Assert.Equal(SampleEvents.FraudSuspected, payload);
    }

    [Fact]
    public void Typed_payload_read_of_another_event_type_fails()
    {
        EventEnvelope envelope = EventEnvelope.Create(SampleEvents.Approved, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Throws<JsonException>(() => EventSerializer.DeserializePayload<AuthorizationDeclined>(envelope));
    }

    [Fact]
    public void Payload_of_an_unknown_event_type_fails()
    {
        EventEnvelope envelope = EventEnvelope.Create(SampleEvents.Approved, Guid.CreateVersion7(), DateTimeOffset.UtcNow)
            with { Type = "authorization.unknown" };

        JsonException exception = Assert.Throws<JsonException>(() => EventSerializer.DeserializePayload(envelope));
        Assert.Contains("authorization.unknown", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Envelope_without_a_required_property_fails()
    {
        const string json = """
            {"eventId":"0199a1b2-0000-7000-8000-0000000000ee","type":"fraud.suspected","version":1,
             "occurredAt":"2026-11-25T13:35:00+00:00","payload":{}}
            """;

        Assert.Throws<JsonException>(() => EventSerializer.Deserialize(json));
    }

    [Fact]
    public void Payload_with_a_null_required_string_fails()
    {
        EventEnvelope envelope = EventSerializer.Deserialize("""
            {"eventId":"0199a1b2-0000-7000-8000-0000000000ee","type":"fraud.suspected","version":1,
             "occurredAt":"2026-11-25T13:35:00+00:00","partitionKey":"0199a1b2-0000-7000-8000-000000000001",
             "payload":{"cardId":"0199a1b2-0000-7000-8000-000000000001",
                        "authorizationId":"0199a1b2-0000-7000-8000-000000000002",
                        "rule":null,"reason":"x","detectedAt":"2026-11-25T13:35:00+00:00"}}
            """);

        Assert.Throws<JsonException>(() => EventSerializer.DeserializePayload(envelope));
    }

    [Fact]
    public void Options_are_read_only()
    {
        Assert.True(EventSerializer.Options.IsReadOnly);
        Assert.Same(JsonNamingPolicy.CamelCase, EventSerializer.Options.PropertyNamingPolicy);
    }
}
