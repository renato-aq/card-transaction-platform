using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Holds;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Ledger.Infrastructure.Persistence.Repositories;

internal sealed class HoldRepository(LedgerDbContext dbContext) : IHoldRepository
{
    public Task<Hold?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Holds.SingleOrDefaultAsync(hold => hold.Id == id, cancellationToken);
    }

    public void Add(Hold hold)
    {
        dbContext.Holds.Add(hold);
    }
}
