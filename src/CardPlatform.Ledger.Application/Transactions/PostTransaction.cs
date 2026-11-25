namespace CardPlatform.Ledger.Application.Transactions;

/// <summary>
/// Posts a double-entry transaction: validates the input, applies it to the accounts and saves the transaction, its
/// entries and the new balances atomically. Every field arrives as raw input and is validated by the handler:
/// <paramref name="Currency"/> is an ISO 4217 code shared by every entry. A <see langword="null"/>
/// <paramref name="Entries"/> is treated as an empty list.
/// </summary>
public sealed record PostTransaction(
    string? Description,
    string? Currency,
    IReadOnlyList<PostTransactionEntry?>? Entries);

/// <summary>
/// One entry of a <see cref="PostTransaction"/>. <paramref name="Direction"/> is <c>debit</c> or <c>credit</c>
/// (case-insensitive); <paramref name="Amount"/> is in the transaction currency.
/// </summary>
public sealed record PostTransactionEntry(Guid? AccountId, string? Direction, decimal? Amount);
