using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Accounts;

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

    public void Add(Account account)
    {
        dbContext.Accounts.Add(account);
    }
}
