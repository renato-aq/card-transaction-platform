using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port for loading and adding <see cref="LedgerTransaction"/> aggregates (with their entries).
/// Changes are persisted by <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface ILedgerTransactionRepository
{
    Task<LedgerTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(LedgerTransaction transaction);
}
