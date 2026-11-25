using CardPlatform.Authorization.Domain.Cards;

namespace CardPlatform.Authorization.Application.Cards;

public sealed record CardDto(Guid Id, Guid LedgerAccountId, string Currency, CardStatus Status)
{
    internal static CardDto From(Card card)
    {
        return new CardDto(card.Id, card.LedgerAccountId, card.Currency.Code, card.Status);
    }
}
