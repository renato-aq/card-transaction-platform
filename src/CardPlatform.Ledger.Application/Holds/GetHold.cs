namespace CardPlatform.Ledger.Application.Holds;

/// <summary>
/// Reads a hold and its state. Fails with <see cref="HoldErrors.HoldNotFoundCode"/> when the hold does not exist. The
/// handler lives in Infrastructure because it reads the database directly.
/// </summary>
public sealed record GetHold(Guid HoldId);
