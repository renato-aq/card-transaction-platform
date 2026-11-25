namespace CardPlatform.Ledger.Application.Holds;

/// <summary>
/// Captures a pending hold, moving its whole amount from the holds account to a merchant account.
/// <paramref name="MerchantAccountId"/> arrives as raw input and is validated by the handler.
/// </summary>
public sealed record CaptureHold(Guid HoldId, Guid? MerchantAccountId);
