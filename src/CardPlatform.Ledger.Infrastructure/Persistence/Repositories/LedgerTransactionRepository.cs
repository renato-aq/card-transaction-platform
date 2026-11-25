using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Transactions;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Ledger.Infrastructure.Persistence.Repositories;

internal sealed class LedgerTransactionRepository(LedgerDbContext dbContext) : ILedgerTransactionRepository
{
    public Task<LedgerTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Entries are auto-included (see LedgerTransactionConfiguration).
        return dbContext.Transactions.SingleOrDefaultAsync(transaction => transaction.Id == id, cancellationToken);
    }

    public void Add(LedgerTransaction transaction)
    {
        dbContext.Transactions.Add(transaction);
    }
}
