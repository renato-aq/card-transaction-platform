using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Common;
using CardPlatform.Ledger.Application.Holds;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Ledger.Infrastructure.Queries;

/// <summary>
/// Reads a hold with a no-tracking projection, without loading the <c>Hold</c> aggregate (pragmatic CQRS, ADR 0004).
/// </summary>
internal sealed class GetHoldHandler(LedgerDbContext dbContext) : IQueryHandler<GetHold, HoldDto>
{
    public async Task<Result<HoldDto>> HandleAsync(GetHold query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var row = await dbContext.Holds
            .AsNoTracking()
            .Where(hold => hold.Id == query.HoldId)
            .Select(static hold => new
            {
                hold.Id,
                hold.CardAccountId,
                hold.Amount.Amount,
                hold.Amount.Currency,
                hold.Status,
                hold.CreatedAt,
                hold.CompletedAt,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return Result.Failure<HoldDto>(HoldErrors.HoldNotFound(query.HoldId));
        }

        return Result.Success(new HoldDto(
            row.Id,
            row.CardAccountId,
            new MoneyDto(row.Amount, row.Currency.Code),
            row.Status,
            row.CreatedAt,
            row.CompletedAt));
    }
}
