using CardPlatform.Authorization.Application.Abstractions.Ledger;

namespace CardPlatform.Authorization.Infrastructure.Ledger;

// Wire contracts of the Ledger API (see CardPlatform.Ledger.Api/Endpoints/*Contracts.cs). The services share no
// code, so the Authorization keeps its own copy of the shapes it uses.

internal sealed record OpenAccountRequest(string Type, string Currency);

internal sealed record PlaceHoldRequest(Guid Id, Guid CardAccountId, decimal Amount, string Currency);

internal sealed record CaptureHoldRequest(Guid MerchantAccountId);

internal sealed record MoneyResponse(decimal Amount, string Currency);

internal sealed record AccountResponse(Guid Id, string Type, string Currency, MoneyResponse Balance);

internal sealed record AccountBalanceResponse(Guid AccountId, MoneyResponse Balance);

internal sealed record HoldResponse(
    Guid Id,
    Guid CardAccountId,
    decimal Amount,
    string Currency,
    LedgerHoldStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>The fields of a Ledger <c>application/problem+json</c> response the client reads.</summary>
internal sealed record LedgerProblem(string? Code, string? Title, string? Detail);
