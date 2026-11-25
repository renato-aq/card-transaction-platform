namespace CardPlatform.Authorization.Application.Abstractions.Ledger;

/// <summary>State of a Ledger hold: <c>Pending</c> until it is captured or voided (both final).</summary>
public enum LedgerHoldStatus
{
    Pending,
    Captured,
    Voided,
}
