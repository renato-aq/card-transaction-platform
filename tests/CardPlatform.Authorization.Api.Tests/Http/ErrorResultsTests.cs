using CardPlatform.Authorization.Api.Http;
using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Domain.Common;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CardPlatform.Authorization.Api.Tests.Http;

public class ErrorResultsTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.BusinessRule, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Unavailable, StatusCodes.Status503ServiceUnavailable)]
    public void Error_type_maps_to_status_code(ErrorType type, int expectedStatus)
    {
        ProblemHttpResult problem = new Error("some_code", "Some message.", type).ToProblem();

        Assert.Equal(expectedStatus, problem.StatusCode);
        Assert.Equal(expectedStatus, problem.ProblemDetails.Status);
        Assert.Equal("Some message.", problem.ProblemDetails.Detail);
        Assert.Equal("some_code", problem.ProblemDetails.Extensions[ErrorResults.CodeExtension]);
    }

    [Fact]
    public void Ledger_unavailable_maps_to_503_with_its_code()
    {
        ProblemHttpResult problem = LedgerErrors.Unavailable().ToProblem();

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
        Assert.Equal("ledger_unavailable", problem.ProblemDetails.Extensions[ErrorResults.CodeExtension]);
    }
}
