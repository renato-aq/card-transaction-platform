using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Messaging.Outbox;

namespace CardPlatform.Authorization.Infrastructure.Persistence.Outbox;

/// <summary>
/// EF Core adapter of <see cref="IOutbox"/>: wraps the event in an <see cref="EventEnvelope"/> and tracks it as an
/// <see cref="OutboxMessage"/> in the <see cref="AuthorizationDbContext"/>, so it is inserted by the same
/// <c>SaveChanges</c>, in the same transaction, as the authorization it describes.
/// </summary>
internal sealed class EfOutbox(AuthorizationDbContext dbContext, TimeProvider timeProvider) : IOutbox
{
    public void Add(IIntegrationEvent integrationEvent, Guid eventId, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        EventEnvelope envelope = EventEnvelope.Create(integrationEvent, eventId, occurredAt);
        dbContext.OutboxMessages.Add(OutboxMessage.Create(envelope, TopicOf(integrationEvent), timeProvider.GetUtcNow()));
    }

    /// <summary>The Authorization only publishes its decisions, on <see cref="Topics.Authorizations"/>.</summary>
    private static string TopicOf(IIntegrationEvent integrationEvent)
    {
        return integrationEvent is IAuthorizationEvent
            ? Topics.Authorizations
            : throw new ArgumentException(
                $"The Authorization does not publish {integrationEvent.GetType().Name} events.",
                nameof(integrationEvent));
    }
}
