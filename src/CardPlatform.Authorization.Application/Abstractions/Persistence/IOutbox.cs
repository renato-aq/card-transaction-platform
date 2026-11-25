using CardPlatform.Contracts;

namespace CardPlatform.Authorization.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port of the transactional outbox: integration events to publish, written in the same database transaction
/// as the change they describe (ADR 0003). Like the repositories, an added event is saved by the next
/// <see cref="IUnitOfWork.SaveChangesAsync"/>, so it is stored if and only if that change is; publishing it is the job of
/// another component.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Adds <paramref name="integrationEvent"/>, identified by <paramref name="eventId"/>, which happened at
    /// <paramref name="occurredAt"/>.
    /// </summary>
    void Add(IIntegrationEvent integrationEvent, Guid eventId, DateTimeOffset occurredAt);
}
