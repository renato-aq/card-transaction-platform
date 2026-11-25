namespace CardPlatform.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port that persists every change tracked by the repositories atomically.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Saves every tracked change in one database transaction.
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">
    /// Another request changed one of the saved aggregates since it was read; nothing was saved.
    /// </exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets every tracked change and loaded aggregate, so the next read comes from the database. Used to retry
    /// a command from a clean state after a <see cref="ConcurrencyConflictException"/>.
    /// </summary>
    void DiscardChanges();
}
