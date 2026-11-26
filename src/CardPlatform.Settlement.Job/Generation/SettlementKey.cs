namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// The position of a settlement entry within its merchant account, in the order of the file
/// (<see cref="SettlementQuery"/>): transaction id, then entry id. The merchant account is not part of it, because the
/// entries are read one merchant at a time (<see cref="SettlementQuery.MerchantEntries"/>). Keys are compared only by the
/// database, as a row value, never in memory: PostgreSQL orders <c>uuid</c> values by their bytes, which
/// <see cref="Guid"/> comparison does not follow.
/// </summary>
/// <param name="TransactionId">The ledger transaction of the credit.</param>
/// <param name="EntryId">The id of the credit entry.</param>
public readonly record struct SettlementKey(Guid TransactionId, long EntryId);
