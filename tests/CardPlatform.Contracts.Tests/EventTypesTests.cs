namespace CardPlatform.Contracts.Tests;

public class EventTypesTests
{
    [Fact]
    public void Every_integration_event_in_the_contracts_is_registered()
    {
        Type[] eventTypes =
        [
            .. typeof(IIntegrationEvent).Assembly.GetTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(type)),
        ];

        Assert.NotEmpty(eventTypes);
        Assert.All(eventTypes, type => Assert.Equal(type, EventTypes.Of(type).ClrType));
        Assert.Equal(eventTypes.Length, EventTypes.All.Count);
    }

    [Fact]
    public void Type_names_are_unique()
    {
        Assert.Equal(EventTypes.All.Count, EventTypes.All.Select(eventType => eventType.Name).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(SampleEvents.AllWithTypeNames), MemberType = typeof(SampleEvents))]
    public void Type_name_resolves_to_the_payload_type(IIntegrationEvent @event, string typeName)
    {
        Assert.True(EventTypes.TryFind(typeName, out EventType? eventType));
        Assert.Equal(@event.GetType(), eventType.ClrType);
    }

    [Fact]
    public void Unknown_type_name_is_not_found()
    {
        Assert.False(EventTypes.TryFind("authorization.unknown", out _));
    }

    [Fact]
    public void Unregistered_type_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => EventTypes.Of(typeof(string)));
    }
}
