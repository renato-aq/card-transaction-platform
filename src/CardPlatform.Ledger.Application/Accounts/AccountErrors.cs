using CardPlatform.Ledger.Domain.Common;

namespace CardPlatform.Ledger.Application.Accounts;

/// <summary>
/// Expected failures of the account use cases that are not domain invariants (input parsing and lookups).
/// </summary>
public static class AccountErrors
{
    public const string InvalidAccountTypeCode = "invalid_account_type";

    public const string AccountNotFoundCode = "account_not_found";

    public static Error InvalidAccountType(string? type)
    {
        return Error.Validation(
            InvalidAccountTypeCode,
            $"Account type '{type}' is not valid. Use one of: card, merchant, settlement.");
    }

    public static Error AccountNotFound(Guid accountId)
    {
        return Error.NotFound(AccountNotFoundCode, $"Account {accountId} was not found.");
    }
}
