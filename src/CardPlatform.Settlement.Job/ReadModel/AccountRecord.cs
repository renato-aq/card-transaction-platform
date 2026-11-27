namespace CardPlatform.Settlement.Job.ReadModel;

/// <summary>A row of the ledger <c>accounts</c> table, as far as the settlement reads it.</summary>
public sealed class AccountRecord
{
    public Guid Id { get; init; }

    /// <summary>The account type as stored by the Ledger, e.g. <c>Merchant</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>ISO 4217 code of the account currency.</summary>
    public string Currency { get; init; } = string.Empty;
}
