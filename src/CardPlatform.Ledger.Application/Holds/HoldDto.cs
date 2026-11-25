using CardPlatform.Ledger.Application.Common;
using CardPlatform.Ledger.Domain.Holds;

namespace CardPlatform.Ledger.Application.Holds;

/// <summary>A hold and its state; <see cref="CompletedAt"/> is <see langword="null"/> while it is pending.</summary>
public sealed record HoldDto(
    Guid Id,
    Guid CardAccountId,
    MoneyDto Amount,
    HoldStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    internal static HoldDto From(Hold hold)
    {
        return new HoldDto(
            hold.Id,
            hold.CardAccountId,
            new MoneyDto(hold.Amount.Amount, hold.Amount.Currency.Code),
            hold.Status,
            hold.CreatedAt,
            hold.CompletedAt);
    }
}
