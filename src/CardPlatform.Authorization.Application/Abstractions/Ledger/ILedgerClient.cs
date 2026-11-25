using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Abstractions.Ledger;

/// <summary>
/// Outbound port to the Ledger service, the source of truth for balances and holds. Expected failures come back as
/// a <see cref="Result"/> with the Ledger error translated into an Authorization error (codes in
/// <see cref="LedgerErrors"/>); when the Ledger cannot be reached in time the error is
/// <see cref="LedgerErrors.UnavailableCode"/> (<see cref="ErrorType.Unavailable"/>).
/// </summary>
public interface ILedgerClient
{
    /// <summary>Opens a card account with a zero balance in <paramref name="currency"/>. Never retried.</summary>
    Task<Result<LedgerAccount>> OpenCardAccountAsync(string currency, CancellationToken cancellationToken = default);

    /// <summary>Returns the current balance of an account.</summary>
    Task<Result<LedgerBalance>> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Places a hold of <paramref name="amount"/> on a card account. Idempotent by <paramref name="holdId"/>:
    /// repeating it with the same data returns the existing hold, so it is safe to retry.
    /// </summary>
    Task<Result<LedgerHold>> PlaceHoldAsync(
        Guid holdId,
        Guid cardAccountId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures a pending hold to a merchant account. Not idempotent (a repeat returns
    /// <see cref="LedgerErrors.HoldNotPendingCode"/>), so it is never retried.
    /// </summary>
    Task<Result<LedgerHold>> CaptureHoldAsync(
        Guid holdId,
        Guid merchantAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Voids a pending hold, returning the money to the card. Not idempotent (a repeat returns
    /// <see cref="LedgerErrors.HoldNotPendingCode"/>), so it is never retried.
    /// </summary>
    Task<Result<LedgerHold>> VoidHoldAsync(Guid holdId, CancellationToken cancellationToken = default);

    /// <summary>Returns a hold and its state (used to reconcile after a capture or void with an unknown outcome).</summary>
    Task<Result<LedgerHold>> GetHoldAsync(Guid holdId, CancellationToken cancellationToken = default);
}
