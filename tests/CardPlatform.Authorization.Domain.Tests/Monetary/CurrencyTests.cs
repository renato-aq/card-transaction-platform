using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Domain.Tests.Monetary;

public class CurrencyTests
{
    [Theory]
    [InlineData("BRL")]
    [InlineData("USD")]
    public void Create_with_three_uppercase_letters_succeeds(string code)
    {
        Result<Currency> result = Currency.Create(code);

        Assert.True(result.IsSuccess);
        Assert.Equal(code, result.Value.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("brl")]
    [InlineData("BR")]
    [InlineData("REAL")]
    [InlineData("B1L")]
    [InlineData("ÁBC")]
    public void Create_with_invalid_code_fails_with_validation_error(string? code)
    {
        Result<Currency> result = Currency.Create(code);

        Assert.True(result.IsFailure);
        Assert.Equal(Currency.InvalidCurrencyCode, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }
}
