using CardPlatform.Contracts;

namespace CardPlatform.Fraud.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port of the Fraud Worker's transactional outbox (ADR 0010): integration events to publish, written in the
/// same database transaction as the change they come from. Like the repositories, an added event is saved by the next
/// <see cref="IUnitOfWork.SaveChangesAsync"/> (or by the transaction in which the consumed event is processed), so it
/// is stored if and only if that change is; publishing it is the job of the outbox publisher.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Adds <paramref name="integrationEvent"/>, identified by <paramref name="eventId"/>, which happened at
    /// <paramref name="occurredAt"/>.
    /// </summary>
    void Add(IIntegrationEvent integrationEvent, Guid eventId, DateTimeOffset occurredAt);
}
