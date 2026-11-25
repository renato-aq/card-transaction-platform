using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Cards;

/// <summary>
/// Expected failures of the card use cases that are not domain invariants (lookups). An invalid currency is
/// <see cref="Domain.Monetary.Currency.InvalidCurrencyCode"/>.
/// </summary>
public static class CardErrors
{
    public const string CardNotFoundCode = "card_not_found";

    public static Error CardNotFound(Guid cardId)
    {
        return Error.NotFound(CardNotFoundCode, $"Card {cardId} was not found.");
    }
}
