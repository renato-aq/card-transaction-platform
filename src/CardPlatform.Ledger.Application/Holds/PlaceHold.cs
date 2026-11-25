namespace CardPlatform.Ledger.Application.Holds;

/// <summary>
/// Places a hold of <paramref name="Amount"/> on a card account. <paramref name="Id"/> is given by the client (the
/// Authorization service uses the authorization id) and makes the command idempotent: repeating it with the same data
/// returns the existing hold without a new ledger transaction. Every field arrives as raw input and is validated by the
/// handler; <paramref name="Currency"/> is an ISO 4217 code.
/// </summary>
public sealed record PlaceHold(Guid? Id, Guid? CardAccountId, decimal? Amount, string? Currency);

/// <summary>
/// Outcome of <see cref="PlaceHold"/>: the hold, and whether this command created it (<see langword="false"/> when the
/// same hold already existed and the command was a repetition).
/// </summary>
public sealed record PlaceHoldResult(HoldDto Hold, bool Created);
