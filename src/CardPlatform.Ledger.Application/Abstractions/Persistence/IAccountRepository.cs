using CardPlatform.Ledger.Domain.Accounts;

namespace CardPlatform.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port for loading and adding <see cref="Account"/> aggregates. Changes are persisted by
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IAccountRepository
{
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the accounts with the given ids. Ids with no account are skipped, so the caller can detect
    /// unknown accounts by comparing the result with the requested ids.
    /// </summary>
    Task<IReadOnlyList<Account>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    void Add(Account account);
}
