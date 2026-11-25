using System.Text;

using CardPlatform.Authorization.Api.Http;
using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Idempotency;
using CardPlatform.Authorization.Domain.Common;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CardPlatform.Authorization.Api.Endpoints;

internal static class AuthorizationEndpoints
{
    /// <summary>Header that makes <c>POST /authorizations</c> idempotent (ADR 0008); required.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder authorizations = endpoints.MapGroup("/authorizations").WithTags("Authorizations");

        authorizations.MapPost("/", AuthorizePurchaseAsync)
            .WithName("AuthorizePurchase")
            .WithSummary(
                "Approves or declines a purchase. Declines are recorded and returned with 201 too. Requires an "
                + "Idempotency-Key header: repeating the key with the same body returns the recorded response.")
            .Produces<AuthorizationResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        authorizations.MapGet("/{id:guid}", GetAuthorizationAsync)
            .WithName("GetAuthorization")
            .WithSummary("Returns an authorization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    // A decline is a business outcome, not an HTTP error: approved and declined authorizations are both 201. The
    // response comes from the idempotency key's stored response (status and body exactly as recorded), so the first
    // answer and every replay are identical; errors are not stored.
    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> AuthorizePurchaseAsync(
        AuthorizePurchaseRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        ICommandHandler<IdempotentAuthorizePurchase, StoredResponse> handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        Result<StoredResponse> result = await handler
            .HandleAsync(
                new IdempotentAuthorizePurchase(
                    idempotencyKey,
                    new AuthorizePurchase(request.CardId, request.MerchantAccountId, request.Amount, request.Currency)),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        StoredResponse response = result.Value;
        if (response.StatusCode == StatusCodes.Status201Created)
        {
            httpContext.Response.Headers.Location = $"/authorizations/{response.AuthorizationId}";
        }

        return TypedResults.Text(response.Body, "application/json", Encoding.UTF8, response.StatusCode);
    }

    private static async Task<Results<Ok<AuthorizationResponse>, ProblemHttpResult>> GetAuthorizationAsync(
        Guid id,
        IQueryHandler<GetAuthorization, AuthorizationDto> handler,
        CancellationToken cancellationToken)
    {
        Result<AuthorizationDto> result = await handler
            .HandleAsync(new GetAuthorization(id), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? result.Error.ToProblem() : TypedResults.Ok(AuthorizationResponse.From(result.Value));
    }
}
