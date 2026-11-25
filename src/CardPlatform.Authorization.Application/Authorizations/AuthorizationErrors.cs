using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// Expected failures of the authorization use cases: input validation and lookups. These are errors, not declines;
/// a declined purchase is a successful result (see <see cref="DeclineReasons"/>). An invalid currency is
/// <see cref="Domain.Monetary.Currency.InvalidCurrencyCode"/>, an amount with more than two decimal places is
/// <see cref="Domain.Monetary.Money.TooManyDecimalPlacesCode"/> and an unknown card is
/// <see cref="Cards.CardErrors.CardNotFoundCode"/>.
/// </summary>
public static class AuthorizationErrors
{
    public const string InvalidCardIdCode = "invalid_card_id";

    public const string InvalidMerchantAccountIdCode = "invalid_merchant_account_id";

    public const string InvalidAmountCode = "invalid_amount";

    public const string AuthorizationNotFoundCode = "authorization_not_found";

    public static readonly Error InvalidCardId = Error.Validation(
        InvalidCardIdCode,
        "The authorization must have a non-empty card id.");

    public static readonly Error InvalidMerchantAccountId = Error.Validation(
        InvalidMerchantAccountIdCode,
        "The authorization must have a non-empty merchant account id.");

    public static readonly Error MissingAmount = Error.Validation(
        InvalidAmountCode,
        "The authorization must have an amount.");

    public static Error NonPositiveAmount(decimal amount)
    {
        return Error.Validation(
            InvalidAmountCode,
            $"The authorization amount must be greater than zero, but it is {amount}.");
    }

    public static Error AuthorizationNotFound(Guid authorizationId)
    {
        return Error.NotFound(AuthorizationNotFoundCode, $"Authorization {authorizationId} was not found.");
    }
}
