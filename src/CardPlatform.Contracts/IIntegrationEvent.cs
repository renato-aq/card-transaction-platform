namespace CardPlatform.Contracts;

/// <summary>
/// Payload of an integration event exchanged through Kafka (ADR 0003). Every event concerns a card, and its
/// <see cref="CardId"/> is the partition key, so all events of a card are delivered in order.
/// </summary>
public interface IIntegrationEvent
{
    Guid CardId { get; }
}
