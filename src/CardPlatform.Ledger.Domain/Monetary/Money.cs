using System.Globalization;

using CardPlatform.Ledger.Domain.Common;

namespace CardPlatform.Ledger.Domain.Monetary;

/// <summary>
/// Immutable monetary amount in a given <see cref="Monetary.Currency"/>, compared by value.
/// Amounts have at most two decimal places and may be negative (balances can be negative).
/// Arithmetic and comparison between different currencies throw <see cref="InvalidOperationException"/>:
/// that is a programming error, since currencies are validated before operating.
/// </summary>
public sealed record Money : IComparable<Money>
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
            return Result.Failure<Money>(new Error(
                TooManyDecimalPlacesCode,
                $"Amount {amount.ToString(CultureInfo.InvariantCulture)} has more than {MaxDecimalPlaces} decimal places."));
        }

        return Result.Success(new Money(amount, currency));
    }

    public static Money Zero(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(0m, currency);
    }

    public static Money operator +(Money left, Money right)
    {
        return Add(left, right);
    }

    public static Money operator -(Money left, Money right)
    {
        return Subtract(left, right);
    }

    public static bool operator <(Money left, Money right)
    {
        return Compare(left, right) < 0;
    }

    public static bool operator >(Money left, Money right)
    {
        return Compare(left, right) > 0;
    }

    public static bool operator <=(Money left, Money right)
    {
        return Compare(left, right) <= 0;
    }

    public static bool operator >=(Money left, Money right)
    {
        return Compare(left, right) >= 0;
    }

    public static Money Add(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money Subtract(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    /// <summary>
    /// Compares amounts of the same currency. A <see langword="null"/> <paramref name="other"/> sorts first.
    /// </summary>
    public int CompareTo(Money? other)
    {
        if (other is null)
        {
            return 1;
        }

        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public override string ToString()
    {
        return $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency.Code}";
    }

    private static int Compare(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.CompareTo(right);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot operate on amounts of different currencies ({left.Currency.Code} and {right.Currency.Code}).");
        }
    }
}
