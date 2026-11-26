namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// One settlement entry as read by <see cref="SettlementPageReader"/>: the columns of its <see cref="SettlementRecord"/>
/// plus the entry id, which completes its <see cref="SettlementKey"/>.
/// </summary>
/// <param name="AccountId">The merchant account credited.</param>
/// <param name="TransactionId">The ledger transaction of the credit.</param>
/// <param name="EntryId">The id of the credit entry.</param>
/// <param name="Amount">Amount credited, in major units of the file currency.</param>
/// <param name="TransactedAt">When the transaction was created, in UTC.</param>
public sealed record SettlementRow(
    Guid AccountId,
    Guid TransactionId,
    long EntryId,
    decimal Amount,
    DateTimeOffset TransactedAt)
{
    /// <summary>The position of the entry within its merchant account, in the order of the file.</summary>
    public SettlementKey Key => new(TransactionId, EntryId);

    /// <summary>The record to write to the file.</summary>
    public SettlementRecord Record => new(AccountId, TransactionId, Amount, TransactedAt);
}
