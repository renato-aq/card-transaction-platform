using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Transactions;

/// <summary>
/// Expected failures of the transaction use cases that are not domain invariants (input parsing).
/// Entries are identified by their zero-based position in the request.
/// </summary>
public static class TransactionErrors
{
    public const string InvalidEntryCode = "invalid_entry";

    public const string InvalidAccountIdCode = "invalid_account_id";

    public const string InvalidDirectionCode = "invalid_direction";

    public static Error InvalidEntry(int index)
    {
        return Error.Validation(InvalidEntryCode, $"Entry {index} must not be null.");
    }

    public static Error InvalidAccountId(int index)
    {
        return Error.Validation(InvalidAccountIdCode, $"Entry {index} must have a non-empty account id.");
    }

    public static Error InvalidDirection(int index, string? direction)
    {
        return Error.Validation(
            InvalidDirectionCode,
            $"Entry {index} has direction '{direction}', which is not valid. Use one of: debit, credit.");
    }

    /// <summary>A missing amount; uses the same code as the domain rule for non-positive amounts.</summary>
    public static Error MissingAmount(int index)
    {
        return Error.Validation(LedgerTransaction.InvalidAmountCode, $"Entry {index} must have an amount.");
    }
}
