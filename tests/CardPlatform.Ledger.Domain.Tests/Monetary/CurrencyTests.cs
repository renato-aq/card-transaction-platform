using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Monetary;

namespace CardPlatform.Ledger.Domain.Tests.Monetary;

public class CurrencyTests
{
    [Theory]
    [InlineData("BRL")]
    [InlineData("USD")]
    [InlineData("EUR")]
    public void Valid_iso_code_creates_currency(string code)
    {
        Result<Currency> result = Currency.Create(code);

        Assert.True(result.IsSuccess);
        Assert.Equal(code, result.Value.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BR")]
    [InlineData("BRLL")]
    [InlineData("brl")]
    [InlineData("Brl")]
    [InlineData("BR1")]
    [InlineData(" BR")]
    [InlineData("ÁBC")]
    public void Invalid_code_is_rejected_with_invalid_currency(string? code)
    {
        Result<Currency> result = Currency.Create(code);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_currency", result.Error.Code);
    }

    [Fact]
    public void Currencies_with_same_code_are_equal()
    {
        Currency first = Currency.Create("BRL").Value;
        Currency second = Currency.Create("BRL").Value;

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, Currency.Create("USD").Value);
    }
}
