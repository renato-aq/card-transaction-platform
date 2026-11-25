namespace CardPlatform.Ledger.Application.Holds;

/// <summary>Voids a pending hold, returning its whole amount from the holds account to the card.</summary>
public sealed record VoidHold(Guid HoldId);
