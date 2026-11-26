using System.Globalization;

using CardPlatform.Fraud.Domain.Common;

namespace CardPlatform.Fraud.Domain.Purchases;

/// <summary>
/// Immutable amount of a purchase in an ISO 4217 currency, compared by value. Follows the rules of the Authorization's
/// <c>Money</c>: at most two decimal places and a currency of three uppercase letters, normalized to a scale of two so
/// <c>10.5</c> and <c>10.50</c> are equal. Amounts of different currencies are never mixed by the fraud rules.
/// </summary>
public sealed record Money
{
    public const string InvalidAmountCode = "invalid_amount";

    public const string InvalidCurrencyCode = "invalid_currency";

    public const int MaxDecimalPlaces = 2;

    private const int CurrencyCodeLength = 3;

    // Adding 0.00m gives every amount a scale of exactly 2, so equality and ToString do not depend on how it was written.
    private const decimal ScaleNormalizer = 0.00m;

    private Money(decimal amount, string currency)
    {
        Amount = amount + ScaleNormalizer;
        Currency = currency;
    }

    public decimal Amount { get; }

    /// <summary>ISO 4217 code (e.g. <c>BRL</c>).</summary>
    public string Currency { get; }

    /// <summary>
    /// A positive amount with at most two decimal places in <paramref name="currency"/>; fails with
    /// <see cref="InvalidAmountCode"/> or <see cref="InvalidCurrencyCode"/> otherwise.
    /// </summary>
    public static Result<Money> Create(decimal amount, string? currency)
    {
        if (currency is not { Length: CurrencyCodeLength } || !currency.All(char.IsAsciiLetterUpper))
        {
            return Result.Failure<Money>(Error.Validation(
                InvalidCurrencyCode,
                $"Currency '{currency}' is not a valid ISO 4217 code (three uppercase letters)."));
        }

        if (amount <= 0 || decimal.Round(amount, MaxDecimalPlaces) != amount)
        {
            return Result.Failure<Money>(Error.Validation(
                InvalidAmountCode,
                $"Amount {amount.ToString(CultureInfo.InvariantCulture)} must be positive with at most {MaxDecimalPlaces} decimal places."));
        }

        return Result.Success(new Money(amount, currency));
    }

    public override string ToString()
    {
        return $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency}";
    }
}
