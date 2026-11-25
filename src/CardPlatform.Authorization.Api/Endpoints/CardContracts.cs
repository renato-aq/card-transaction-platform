using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Domain.Cards;

namespace CardPlatform.Authorization.Api.Endpoints;

/// <summary>Body of <c>POST /cards</c>. <see cref="Currency"/> is an ISO 4217 code (e.g. <c>BRL</c>).</summary>
public sealed record CreateCardRequest(string? Currency);

/// <summary>A card; <see cref="Status"/> is serialized in camelCase (<c>active</c>, <c>blocked</c>).</summary>
public sealed record CardResponse(Guid Id, Guid LedgerAccountId, string Currency, CardStatus Status)
{
    internal static CardResponse From(CardDto card)
    {
        return new CardResponse(card.Id, card.LedgerAccountId, card.Currency, card.Status);
    }
}
