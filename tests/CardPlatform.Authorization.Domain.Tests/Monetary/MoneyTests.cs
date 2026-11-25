using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Domain.Tests.Monetary;

public class MoneyTests
{
    [Theory]
    [InlineData("10", "10.00")]
    [InlineData("10.5", "10.50")]
    [InlineData("10.500", "10.50")]
    [InlineData("-3.25", "-3.25")]
    public void Create_normalizes_the_amount_to_two_decimal_places(string amount, string expected)
    {
        Money money = Money.Create(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "BRL").Value;

        Assert.Equal(expected, money.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("BRL", money.Currency.Code);
    }

    [Fact]
    public void Create_with_more_than_two_decimal_places_fails()
    {
        Result<Money> result = Money.Create(10.001m, "BRL");

        Assert.True(result.IsFailure);
        Assert.Equal(Money.TooManyDecimalPlacesCode, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Create_with_invalid_currency_fails()
    {
        Result<Money> result = Money.Create(10m, "brl");

        Assert.True(result.IsFailure);
        Assert.Equal(Currency.InvalidCurrencyCode, result.Error.Code);
    }

    [Fact]
    public void Amounts_with_the_same_value_and_currency_are_equal()
    {
        Assert.Equal(Money.Create(10.5m, "BRL").Value, Money.Create(10.50m, "BRL").Value);
        Assert.NotEqual(Money.Create(10m, "BRL").Value, Money.Create(10m, "USD").Value);
    }

    [Fact]
    public void To_string_shows_amount_and_currency()
    {
        Assert.Equal("10.50 BRL", Money.Create(10.5m, "BRL").Value.ToString());
    }
}
