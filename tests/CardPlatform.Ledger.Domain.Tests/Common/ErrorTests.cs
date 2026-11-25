using CardPlatform.Ledger.Domain.Common;

namespace CardPlatform.Ledger.Domain.Tests.Common;

public class ErrorTests
{
    [Fact]
    public void Factories_set_the_error_type()
    {
        Assert.Equal(ErrorType.Validation, Error.Validation("code", "Message.").Type);
        Assert.Equal(ErrorType.NotFound, Error.NotFound("code", "Message.").Type);
        Assert.Equal(ErrorType.BusinessRule, Error.BusinessRule("code", "Message.").Type);
        Assert.Equal(ErrorType.Conflict, Error.Conflict("code", "Message.").Type);
    }

    [Fact]
    public void Factories_keep_code_and_message()
    {
        Error error = Error.NotFound("account_not_found", "Account was not found.");

        Assert.Equal("account_not_found", error.Code);
        Assert.Equal("Account was not found.", error.Message);
    }

    [Fact]
    public void Undefined_error_type_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Error("code", "Message.", (ErrorType)42));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_code_is_rejected(string code)
    {
        Assert.Throws<ArgumentException>(() => Error.Validation(code, "Message."));
    }
}
