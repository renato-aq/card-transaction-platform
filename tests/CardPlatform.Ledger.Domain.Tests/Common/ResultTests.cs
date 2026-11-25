using CardPlatform.Ledger.Domain.Common;

namespace CardPlatform.Ledger.Domain.Tests.Common;

public class ResultTests
{
    private static readonly Error SomeError = Error.BusinessRule("some_error", "Something went wrong.");

    [Fact]
    public void Success_result_has_no_error()
    {
        Result result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_result_exposes_its_error()
    {
        Result result = Result.Failure(SomeError);

        Assert.True(result.IsFailure);
        Assert.Equal("some_error", result.Error.Code);
        Assert.Equal("Something went wrong.", result.Error.Message);
    }

    [Fact]
    public void Successful_generic_result_exposes_its_value()
    {
        Result<int> result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Accessing_value_of_failed_result_throws()
    {
        Result<int> result = Result.Failure<int>(SomeError);

        Assert.True(result.IsFailure);
        Assert.Same(SomeError, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }
}
