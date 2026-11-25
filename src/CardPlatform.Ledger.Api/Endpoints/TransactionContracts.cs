using CardPlatform.Ledger.Application.Transactions;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Api.Endpoints;

/// <summary>Body of <c>POST /transactions</c>: every entry is in <see cref="Currency"/>.</summary>
public sealed record PostTransactionRequest(
    string? Description,
    string? Currency,
    IReadOnlyList<PostTransactionEntryRequest?>? Entries);

/// <summary>An entry of <see cref="PostTransactionRequest"/>; <see cref="Direction"/> is <c>debit</c> or <c>credit</c>.</summary>
public sealed record PostTransactionEntryRequest(Guid? AccountId, string? Direction, decimal? Amount);

public sealed record TransactionResponse(
    Guid Id,
    string? Description,
    string Currency,
    DateTimeOffset CreatedAt,
    IReadOnlyList<EntryResponse> Entries)
{
    internal static TransactionResponse From(LedgerTransactionDto transaction)
    {
        return new TransactionResponse(
            transaction.Id,
            transaction.Description,
            transaction.Currency,
            transaction.CreatedAt,
            [.. transaction.Entries.Select(static entry => new EntryResponse(entry.AccountId, entry.Direction, entry.Amount))]);
    }
}

/// <summary>An entry; <see cref="Direction"/> is serialized in camelCase (<c>debit</c>, <c>credit</c>).</summary>
public sealed record EntryResponse(Guid AccountId, EntryDirection Direction, decimal Amount);
