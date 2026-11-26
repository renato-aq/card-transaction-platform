namespace CardPlatform.Contracts.Tests;

public class EventEnvelopeRoundTripTests
{
    private static readonly Guid EventId = Guid.Parse("0199a1b2-0000-7000-8000-0000000000ee");

    private static readonly DateTimeOffset OccurredAt = new DateTimeOffset(2026, 11, 25, 13, 35, 0, TimeSpan.Zero)
        .AddTicks(987650);

    [Theory]
    [MemberData(nameof(SampleEvents.AllWithTypeNames), MemberType = typeof(SampleEvents))]
    public void Event_survives_a_round_trip_through_json_text(IIntegrationEvent @event, string typeName)
    {
        EventEnvelope envelope = EventEnvelope.Create(@event, EventId, OccurredAt);

        EventEnvelope read = EventSerializer.Deserialize(EventSerializer.Serialize(envelope));

        AssertEnvelope(read, typeName);
        Assert.Equal(@event, EventSerializer.DeserializePayload(read));
    }

    [Theory]
    [MemberData(nameof(SampleEvents.AllWithTypeNames), MemberType = typeof(SampleEvents))]
    public void Event_survives_a_round_trip_through_utf8_bytes(IIntegrationEvent @event, string typeName)
    {
        EventEnvelope envelope = EventEnvelope.Create(@event, EventId, OccurredAt);

        EventEnvelope read = EventSerializer.Deserialize(EventSerializer.SerializeToUtf8Bytes(envelope));

        AssertEnvelope(read, typeName);
        Assert.Equal(@event, EventSerializer.DeserializePayload(read));
    }

    [Fact]
    public void Round_trip_keeps_the_amount_scale_and_the_timestamp_offset()
    {
        EventEnvelope envelope = EventEnvelope.Create(SampleEvents.Approved, EventId, OccurredAt);

        Authorizations.AuthorizationApproved read = EventSerializer.DeserializePayload<Authorizations.AuthorizationApproved>(
            EventSerializer.Deserialize(EventSerializer.Serialize(envelope)));

        Assert.Equal("150.75", read.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(SampleEvents.Approved.CreatedAt.Offset, read.CreatedAt.Offset);
        Assert.Equal(SampleEvents.Approved.CreatedAt.UtcTicks, read.CreatedAt.UtcTicks);
    }

    private static void AssertEnvelope(EventEnvelope read, string typeName)
    {
        Assert.Equal(EventId, read.EventId);
        Assert.Equal(typeName, read.Type);
        Assert.Equal(1, read.Version);
        Assert.Equal(OccurredAt, read.OccurredAt);
        Assert.Equal(OccurredAt.Offset, read.OccurredAt.Offset);
        Assert.Equal(SampleEvents.CardId.ToString(), read.PartitionKey);
    }
}
