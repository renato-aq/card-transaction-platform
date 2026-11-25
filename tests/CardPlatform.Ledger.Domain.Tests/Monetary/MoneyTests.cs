using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Monetary;

namespace CardPlatform.Ledger.Domain.Tests.Monetary;

public class MoneyTests
{
    private static readonly Currency Brl = Currency.Create("BRL").Value;

    private static readonly Currency Usd = Currency.Create("USD").Value;

    private static Money Reais(decimal amount)
    {
        return Money.Create(amount, Brl).Value;
    }

    private static Money Dollars(decimal amount)
    {
        return Money.Create(amount, Usd).Value;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(10.5)]
    [InlineData(10.55)]
    [InlineData(-25.99)]
    public void Valid_amount_and_currency_create_money(decimal amount)
    {
        Result<Money> result = Money.Create(amount, "BRL");

        Assert.True(result.IsSuccess);
        Assert.Equal(amount, result.Value.Amount);
        Assert.Equal(Brl, result.Value.Currency);
    }

    [Fact]
    public void Trailing_zeros_beyond_two_decimal_places_are_accepted()
    {
        Result<Money> result = Money.Create(10.500m, "BRL");

        Assert.True(result.IsSuccess);
        Assert.Equal(10.5m, result.Value.Amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("brl")]
    [InlineData("BR")]
    [InlineData("12A")]
    public void Invalid_currency_is_rejected(string? currencyCode)
    {
        Result<Money> result = Money.Create(10m, currencyCode);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_currency", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Theory]
    [InlineData(10.005)]
    [InlineData(0.001)]
    [InlineData(-1.999)]
    public void Amount_with_more_than_two_decimal_places_is_rejected(decimal amount)
    {
        Result<Money> result = Money.Create(amount, "BRL");

        Assert.True(result.IsFailure);
        Assert.Equal("too_many_decimal_places", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Money_with_same_amount_and_currency_is_equal()
    {
        Money first = Reais(10.5m);
        Money second = Money.Create(10.50m, "BRL").Value;

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Money_with_different_amount_or_currency_is_not_equal()
    {
        Assert.NotEqual(Reais(10m), Reais(10.01m));
        Assert.NotEqual(Reais(10m), Dollars(10m));
        Assert.True(Reais(10m) != Dollars(10m));
    }

    [Fact]
    public void Amount_is_normalized_to_two_decimal_places()
    {
        Assert.Equal("10.50 BRL", Reais(10.5m).ToString());
        Assert.Equal("10.50 BRL", Money.Create(10.500m, "BRL").Value.ToString());
        Assert.Equal("7.00 BRL", Reais(7m).ToString());
    }

    [Fact]
    public void Zero_creates_zero_amount_in_given_currency()
    {
        Money zero = Money.Zero(Brl);

        Assert.Equal(0m, zero.Amount);
        Assert.Equal(Brl, zero.Currency);
        Assert.Equal(Reais(0m), zero);
    }

    [Fact]
    public void Adding_money_of_same_currency_sums_amounts()
    {
        Assert.Equal(Reais(15.75m), Reais(10.50m) + Reais(5.25m));
        Assert.Equal(Reais(15.75m), Money.Add(Reais(10.50m), Reais(5.25m)));
        Assert.Equal(Reais(10m), Reais(10m) + Money.Zero(Brl));
    }

    [Fact]
    public void Subtracting_money_of_same_currency_subtracts_amounts()
    {
        Assert.Equal(Reais(5.25m), Reais(10.50m) - Reais(5.25m));
        Assert.Equal(Reais(5.25m), Money.Subtract(Reais(10.50m), Reais(5.25m)));
    }

    [Fact]
    public void Subtraction_can_produce_negative_amount()
    {
        Money result = Reais(5m) - Reais(7.50m);

        Assert.Equal(-2.50m, result.Amount);
    }

    [Fact]
    public void Money_of_same_currency_is_compared_by_amount()
    {
        Money smaller = Reais(9.99m);
        Money larger = Reais(10m);

        Assert.True(smaller < larger);
        Assert.True(larger > smaller);
        Assert.True(smaller <= larger);
        Assert.True(larger >= smaller);
        Assert.True(larger <= Reais(10m));
        Assert.True(larger >= Reais(10m));
        Assert.False(smaller > larger);
        Assert.False(larger < smaller);
        Assert.True(smaller.CompareTo(larger) < 0);
        Assert.Equal(0, larger.CompareTo(Reais(10m)));
        Assert.True(Reais(-1m) < Money.Zero(Brl));
    }

    [Fact]
    public void Adding_money_of_different_currencies_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Reais(10m) + Dollars(10m));
        Assert.Throws<InvalidOperationException>(() => Money.Add(Reais(10m), Dollars(10m)));
    }

    [Fact]
    public void Subtracting_money_of_different_currencies_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Reais(10m) - Dollars(10m));
        Assert.Throws<InvalidOperationException>(() => Money.Subtract(Reais(10m), Dollars(10m)));
    }

    [Fact]
    public void Comparing_money_of_different_currencies_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Reais(10m) < Dollars(10m));
        Assert.Throws<InvalidOperationException>(() => Reais(10m) > Dollars(10m));
        Assert.Throws<InvalidOperationException>(() => Reais(10m) <= Dollars(10m));
        Assert.Throws<InvalidOperationException>(() => Reais(10m) >= Dollars(10m));
        Assert.Throws<InvalidOperationException>(() => Reais(10m).CompareTo(Dollars(10m)));
    }
}
