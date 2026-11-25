using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Common;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Monetary;

namespace CardPlatform.Ledger.Application.Accounts;

public sealed class OpenAccountHandler(
    IAccountRepository accounts,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<OpenAccount, AccountDto>
{
    public async Task<Result<AccountDto>> HandleAsync(OpenAccount command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!TryParseAccountType(command.Type, out AccountType type))
        {
            return Result.Failure<AccountDto>(AccountErrors.InvalidAccountType(command.Type));
        }

        Result<Currency> currency = Currency.Create(command.Currency);
        if (currency.IsFailure)
        {
            return Result.Failure<AccountDto>(currency.Error);
        }

        Account account = Account.Open(type, currency.Value, timeProvider.GetUtcNow());
        accounts.Add(account);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new AccountDto(
            account.Id,
            account.Type,
            account.Currency.Code,
            new MoneyDto(account.Balance.Amount, account.Balance.Currency.Code)));
    }

    // Matches the enum names only (case-insensitive). Enum.TryParse is not used because it also accepts
    // numeric strings such as "1" or "42", which are not valid account types for clients.
    private static bool TryParseAccountType(string? value, out AccountType type)
    {
        foreach (AccountType candidate in Enum.GetValues<AccountType>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }
}
