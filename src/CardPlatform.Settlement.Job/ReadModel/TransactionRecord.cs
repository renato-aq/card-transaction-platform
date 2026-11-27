namespace CardPlatform.Settlement.Job.ReadModel;

/// <summary>A row of the ledger <c>transactions</c> table.</summary>
public sealed class TransactionRecord
{
    public Guid Id { get; init; }

    /// <summary>The description, or <see langword="null"/>: the Ledger stores a blank description as NULL.</summary>
    public string? Description { get; init; }

    /// <summary>When the transaction was created, in UTC and whole microseconds.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
