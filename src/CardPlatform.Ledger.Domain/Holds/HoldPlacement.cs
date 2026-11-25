using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Domain.Holds;

/// <summary>
/// Outcome of <see cref="Hold.Place"/>: the pending hold and the transaction that moved its amount from the card to
/// the holds account. Both are persisted together, with the two accounts.
/// </summary>
public sealed record HoldPlacement(Hold Hold, LedgerTransaction Transaction);
