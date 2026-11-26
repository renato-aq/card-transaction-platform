using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Domain.Tests.Purchases;

public class MoneyTests
{
    [Fact]
    public void Amounts_written_with_different_scales_are_equal()
    {
        Assert.Equal(Money.Create(10.5m, "BRL").Value, Money.Create(10.500m, "BRL").Value);
        Assert.Equal("10.50 BRL", Money.Create(10.5m, "BRL").Value.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.001)]
    public void Rejects_amounts_that_are_not_positive_or_have_more_than_two_decimal_places(decimal amount)
    {
        Assert.Equal(Money.InvalidAmountCode, Money.Create(amount, "BRL").Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("brl")]
    [InlineData("BRLX")]
    [InlineData("B1L")]
    public void Rejects_currencies_that_are_not_three_uppercase_letters(string? currency)
    {
        Assert.Equal(Money.InvalidCurrencyCode, Money.Create(10m, currency).Error.Code);
    }
}
