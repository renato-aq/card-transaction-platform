using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Accounts;
using CardPlatform.Ledger.Application.Common;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Ledger.Infrastructure.Queries;

/// <summary>
/// Reads the balance with a no-tracking projection, without loading the <c>Account</c> aggregate
/// (pragmatic CQRS, ADR 0004).
/// </summary>
internal sealed class GetAccountBalanceHandler(LedgerDbContext dbContext)
    : IQueryHandler<GetAccountBalance, AccountBalanceDto>
{
    public async Task<Result<AccountBalanceDto>> HandleAsync(
        GetAccountBalance query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var row = await dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.Id == query.AccountId)
            .Select(static account => new { account.Id, account.Balance.Amount, account.Currency })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return Result.Failure<AccountBalanceDto>(AccountErrors.AccountNotFound(query.AccountId));
        }

        return Result.Success(new AccountBalanceDto(row.Id, new MoneyDto(row.Amount, row.Currency.Code)));
    }
}
