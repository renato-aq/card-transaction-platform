using CardPlatform.Ledger.Application.Transactions;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Holds;

/// <summary>
/// Expected failures of the hold use cases that are not domain invariants (input parsing, lookups, idempotency and
/// the type of the card account chosen by the client).
/// </summary>
public static class HoldErrors
{
    public const string InvalidHoldIdCode = "invalid_hold_id";

    public const string HoldNotFoundCode = "hold_not_found";

    public const string HoldIdConflictCode = "hold_id_conflict";

    public const string NotACardAccountCode = "not_a_card_account";

    public static readonly Error InvalidHoldId = Error.Validation(InvalidHoldIdCode, "The hold must have a non-empty id.");

    public static readonly Error InvalidCardAccountId = Error.Validation(
        TransactionErrors.InvalidAccountIdCode,
        "The hold must have a non-empty card account id.");

    public static readonly Error InvalidMerchantAccountId = Error.Validation(
        TransactionErrors.InvalidAccountIdCode,
        "The capture must have a non-empty merchant account id.");

    /// <summary>A missing amount; uses the same code as the ledger rule for non-positive amounts.</summary>
    public static readonly Error MissingAmount = Error.Validation(
        LedgerTransaction.InvalidAmountCode,
        "The hold must have an amount.");

    public static Error NonPositiveAmount(decimal amount)
    {
        return Error.Validation(
            LedgerTransaction.InvalidAmountCode,
            $"The hold amount must be greater than zero, but it is {amount}.");
    }

    public static Error HoldNotFound(Guid holdId)
    {
        return Error.NotFound(HoldNotFoundCode, $"Hold {holdId} was not found.");
    }

    /// <summary>
    /// A hold with this id already exists with a different card account or amount, so the request is not a repetition
    /// of the one that created it.
    /// </summary>
    public static Error HoldIdConflict(Guid holdId)
    {
        return Error.Conflict(
            HoldIdConflictCode,
            $"Hold {holdId} already exists with a different card account or amount.");
    }

    /// <summary>The card account comes from the client, so an account of another type is a business error.</summary>
    public static Error NotACardAccount(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return Error.BusinessRule(
            NotACardAccountCode,
            $"Account {account.Id} is a {account.Type} account; a hold can only be placed on a Card account.");
    }
}
