using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Monetary;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Ledger.Infrastructure.Persistence.Repositories;

internal sealed class AccountRepository(LedgerDbContext dbContext) : IAccountRepository
{
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Accounts.SingleOrDefaultAsync(account => account.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Account>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return await dbContext.Accounts
            .Where(account => ids.Contains(account.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Account?> GetHoldsAccountAsync(Currency currency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currency);

        // At most one row matches: a unique partial index allows one holds account per currency.
        return dbContext.Accounts.SingleOrDefaultAsync(
            account => account.Type == AccountType.Holds && account.Currency == currency,
            cancellationToken);
    }

    public void Add(Account account)
    {
        dbContext.Accounts.Add(account);
    }
}
