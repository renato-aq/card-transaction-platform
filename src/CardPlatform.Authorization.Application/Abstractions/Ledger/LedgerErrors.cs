using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Abstractions.Ledger;

/// <summary>
/// Errors that come from the Ledger. The adapter keeps the Ledger's error code and classifies it by HTTP status
/// (400 → <see cref="ErrorType.Validation"/>, 404 → <see cref="ErrorType.NotFound"/>,
/// 409 → <see cref="ErrorType.Conflict"/>, 422 → <see cref="ErrorType.BusinessRule"/>); the codes the Authorization
/// reacts to are listed here. A Ledger that cannot be reached in time is <see cref="Unavailable"/>.
/// </summary>
public static class LedgerErrors
{
    public const string UnavailableCode = "ledger_unavailable";

    public const string InsufficientFundsCode = "insufficient_funds";

    public const string HoldNotPendingCode = "hold_not_pending";

    public const string CurrencyMismatchCode = "currency_mismatch";

    public const string NotACardAccountCode = "not_a_card_account";

    public const string NotAMerchantAccountCode = "not_a_merchant_account";

    public const string AccountNotFoundCode = "account_not_found";

    public const string HoldNotFoundCode = "hold_not_found";

    public const string HoldIdConflictCode = "hold_id_conflict";

    public const string ConcurrencyConflictCode = "concurrency_conflict";

    /// <summary>The Ledger did not answer in time, the circuit is open or it kept failing (5xx).</summary>
    public static Error Unavailable()
    {
        return Error.Unavailable(UnavailableCode, "The Ledger is unavailable. Try again later.");
    }
}
