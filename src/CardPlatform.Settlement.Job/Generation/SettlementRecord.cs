namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// One settlement entry as read from the ledger: a credit of <paramref name="Amount"/> to a merchant account. The
/// <see cref="SettlementFileWriter"/> numbers the records in the order they are written.
/// </summary>
/// <param name="MerchantAccountId">The merchant account credited.</param>
/// <param name="TransactionId">The ledger transaction of the credit.</param>
/// <param name="Amount">Amount credited, in major units of the file currency.</param>
/// <param name="TransactedAt">When the transaction was created, in UTC.</param>
public readonly record struct SettlementRecord(
    Guid MerchantAccountId,
    Guid TransactionId,
    decimal Amount,
    DateTimeOffset TransactedAt);
