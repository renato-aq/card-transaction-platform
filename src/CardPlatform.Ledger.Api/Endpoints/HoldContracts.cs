using CardPlatform.Ledger.Application.Holds;
using CardPlatform.Ledger.Domain.Holds;

namespace CardPlatform.Ledger.Api.Endpoints;

/// <summary>
/// Body of <c>POST /holds</c>. <see cref="Id"/> is chosen by the client and makes the request idempotent:
/// repeating it with the same data returns the existing hold.
/// </summary>
public sealed record PlaceHoldRequest(Guid? Id, Guid? CardAccountId, decimal? Amount, string? Currency);

/// <summary>Body of <c>POST /holds/{id}/capture</c>.</summary>
public sealed record CaptureHoldRequest(Guid? MerchantAccountId);

/// <summary>
/// A hold; <see cref="Status"/> is serialized in camelCase (<c>pending</c>, <c>captured</c>, <c>voided</c>) and
/// <see cref="CompletedAt"/> is <see langword="null"/> while the hold is pending.
/// </summary>
public sealed record HoldResponse(
    Guid Id,
    Guid CardAccountId,
    decimal Amount,
    string Currency,
    HoldStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    internal static HoldResponse From(HoldDto hold)
    {
        return new HoldResponse(
            hold.Id,
            hold.CardAccountId,
            hold.Amount.Amount,
            hold.Amount.Currency,
            hold.Status,
            hold.CreatedAt,
            hold.CompletedAt);
    }
}
