using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Monetary;

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

    /// <summary>
    /// Loads the <see cref="AccountType.Holds"/> account of <paramref name="currency"/> (there is at most one per
    /// currency, ADR 0007), or <see langword="null"/> when it was not created yet.
    /// </summary>
    Task<Account?> GetHoldsAccountAsync(Currency currency, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new account, inserted by the next <see cref="IUnitOfWork.SaveChangesAsync"/>. Adding a second
    /// <see cref="AccountType.Holds"/> account for a currency makes that save throw
    /// <see cref="ConcurrencyConflictException"/>: another request created it first, and a retry finds it.
    /// </summary>
    void Add(Account account);
}
