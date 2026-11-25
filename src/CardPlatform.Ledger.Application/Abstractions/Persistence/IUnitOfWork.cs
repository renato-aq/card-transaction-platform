namespace CardPlatform.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port that persists every change tracked by the repositories atomically.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
