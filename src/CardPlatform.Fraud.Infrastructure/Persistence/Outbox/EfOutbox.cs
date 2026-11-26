using CardPlatform.Contracts;
using CardPlatform.Contracts.Fraud;
using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Messaging.Outbox;

namespace CardPlatform.Fraud.Infrastructure.Persistence.Outbox;

/// <summary>
/// EF Core adapter of <see cref="IOutbox"/>: wraps the event in an <see cref="EventEnvelope"/> (key = card id) and
/// tracks it as an <see cref="OutboxMessage"/> in the <see cref="FraudDbContext"/>, so it is inserted by the same
/// <c>SaveChanges</c>, in the same transaction, as the purchase it comes from and the processed mark of the consumed
/// event (ADR 0010, ADR 0011).
/// </summary>
internal sealed class EfOutbox(FraudDbContext dbContext, TimeProvider timeProvider) : IOutbox
{
    public void Add(IIntegrationEvent integrationEvent, Guid eventId, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        EventEnvelope envelope = EventEnvelope.Create(integrationEvent, eventId, occurredAt);
        dbContext.OutboxMessages.Add(OutboxMessage.Create(envelope, TopicOf(integrationEvent), timeProvider.GetUtcNow()));
    }

    /// <summary>The Fraud Worker only publishes its suspicions, on <see cref="Topics.FraudSignals"/>.</summary>
    private static string TopicOf(IIntegrationEvent integrationEvent)
    {
        return integrationEvent is FraudSuspected
            ? Topics.FraudSignals
            : throw new ArgumentException(
                $"The Fraud Worker does not publish {integrationEvent.GetType().Name} events.",
                nameof(integrationEvent));
    }
}
