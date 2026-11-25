using CardPlatform.Authorization.Api.Http;
using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Domain.Common;

using Microsoft.AspNetCore.Http.HttpResults;

namespace CardPlatform.Authorization.Api.Endpoints;

internal static class AuthorizationEndpoints
{
    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder authorizations = endpoints.MapGroup("/authorizations").WithTags("Authorizations");

        authorizations.MapPost("/", AuthorizePurchaseAsync)
            .WithName("AuthorizePurchase")
            .WithSummary("Approves or declines a purchase. Declines are recorded and returned with 201 too.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        authorizations.MapGet("/{id:guid}", GetAuthorizationAsync)
            .WithName("GetAuthorization")
            .WithSummary("Returns an authorization.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    // A decline is a business outcome, not an HTTP error: approved and declined authorizations are both 201.
    private static async Task<Results<Created<AuthorizationResponse>, ProblemHttpResult>> AuthorizePurchaseAsync(
        AuthorizePurchaseRequest request,
        ICommandHandler<AuthorizePurchase, AuthorizationDto> handler,
        CancellationToken cancellationToken)
    {
        Result<AuthorizationDto> result = await handler
            .HandleAsync(
                new AuthorizePurchase(request.CardId, request.MerchantAccountId, request.Amount, request.Currency),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        return TypedResults.Created(
            $"/authorizations/{result.Value.Id}",
            AuthorizationResponse.From(result.Value));
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
