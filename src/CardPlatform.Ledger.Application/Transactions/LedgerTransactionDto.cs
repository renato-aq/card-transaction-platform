using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Transactions;

public sealed record LedgerTransactionDto(
    Guid Id,
    string? Description,
    string Currency,
    DateTimeOffset CreatedAt,
    IReadOnlyList<EntryDto> Entries);

/// <summary>An entry; <see cref="Amount"/> is in the currency of its transaction.</summary>
public sealed record EntryDto(Guid AccountId, EntryDirection Direction, decimal Amount);
