namespace CardPlatform.Settlement.Job.FileLayout;

/// <summary>
/// A settled transaction: one detail line of the file.
/// </summary>
/// <param name="SequenceNumber">Position of the line among the details, starting at 1.</param>
/// <param name="TransactionId">The ledger transaction id.</param>
/// <param name="MerchantAccountId">The merchant account credited by the transaction.</param>
/// <param name="Amount">Amount in major units of the file currency (e.g. reais), never negative.</param>
/// <param name="TransactedAt">When the transaction happened; written in UTC, to the microsecond.</param>
public sealed record SettlementDetail(
    long SequenceNumber,
    Guid TransactionId,
    Guid MerchantAccountId,
    decimal Amount,
    DateTimeOffset TransactedAt);
