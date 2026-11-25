using CardPlatform.Authorization.Domain.Common;

using Microsoft.AspNetCore.Http.HttpResults;

namespace CardPlatform.Authorization.Api.Http;

/// <summary>
/// Translates an expected failure (<see cref="Error"/>) into an RFC 9457 <c>ProblemDetails</c> response, with the
/// error code in the <c>code</c> extension. <see cref="ErrorType.Unavailable"/> (e.g. <c>ledger_unavailable</c>)
/// becomes <c>503 Service Unavailable</c>.
/// </summary>
internal static class ErrorResults
{
    public const string CodeExtension = "code";

    public static ProblemHttpResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        (int statusCode, string title) = error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "Invalid request"),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "Resource not found"),
            ErrorType.BusinessRule => (StatusCodes.Status422UnprocessableEntity, "Business rule violated"),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            ErrorType.Unavailable => (StatusCodes.Status503ServiceUnavailable, "Service unavailable"),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Type, "Unknown error type."),
        };

        return TypedResults.Problem(
            detail: error.Message,
            statusCode: statusCode,
            title: title,
            extensions: new Dictionary<string, object?> { [CodeExtension] = error.Code });
    }
}
