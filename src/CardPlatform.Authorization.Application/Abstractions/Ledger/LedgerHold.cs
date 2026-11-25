namespace CardPlatform.Authorization.Application.Abstractions.Ledger;

/// <summary>A hold in the Ledger; <see cref="CompletedAt"/> is <see langword="null"/> while it is pending.</summary>
public sealed record LedgerHold(
    Guid Id,
    Guid CardAccountId,
    decimal Amount,
    string Currency,
    LedgerHoldStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
