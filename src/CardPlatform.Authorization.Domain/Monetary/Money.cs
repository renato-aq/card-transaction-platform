using System.Globalization;

using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Domain.Monetary;

/// <summary>
/// Immutable monetary amount in a given <see cref="Monetary.Currency"/>, compared by value. Mirrors the Ledger's
/// <c>Money</c>: amounts have at most two decimal places (<see cref="TooManyDecimalPlacesCode"/>), so an amount the
/// Ledger would reject is rejected before it is called. Whether an amount must be positive is up to the use case.
/// </summary>
public sealed record Money
{
    public const string TooManyDecimalPlacesCode = "too_many_decimal_places";

    public const int MaxDecimalPlaces = 2;

    // Adding 0.00m gives every amount a scale of exactly 2 (10.5m and 10.500m both become 10.50m),
    // so the representation, ToString and hash code do not depend on how the amount was written.
    private const decimal ScaleNormalizer = 0.00m;

    private Money(decimal amount, Currency currency)
    {
        Amount = decimal.Round(amount, MaxDecimalPlaces) + ScaleNormalizer;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public static Result<Money> Create(decimal amount, string? currencyCode)
    {
        Result<Currency> currency = Currency.Create(currencyCode);
        if (currency.IsFailure)
        {
            return Result.Failure<Money>(currency.Error);
        }

        return Create(amount, currency.Value);
    }

    public static Result<Money> Create(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (decimal.Round(amount, MaxDecimalPlaces) != amount)
        {
            return Result.Failure<Money>(Error.Validation(
                TooManyDecimalPlacesCode,
                $"Amount {amount.ToString(CultureInfo.InvariantCulture)} has more than {MaxDecimalPlaces} decimal places."));
        }

        return Result.Success(new Money(amount, currency));
    }

    public override string ToString()
    {
        return $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency.Code}";
    }
}
