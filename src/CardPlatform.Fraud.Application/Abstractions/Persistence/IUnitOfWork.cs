namespace CardPlatform.Fraud.Application.Abstractions.Persistence;

/// <summary>Outbound port that persists every change tracked by the repositories atomically.</summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves every tracked change in one database transaction (or in the transaction already open, such as the one in
    /// which an event is processed and marked as processed).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
