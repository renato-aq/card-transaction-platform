using CardPlatform.Authorization.Api.Http;
using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Domain.Common;

using Microsoft.AspNetCore.Http.HttpResults;

namespace CardPlatform.Authorization.Api.Endpoints;

internal static class CardEndpoints
{
    public static IEndpointRouteBuilder MapCardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder cards = endpoints.MapGroup("/cards").WithTags("Cards");

        cards.MapPost("/", CreateCardAsync)
            .WithName("CreateCard")
            .WithSummary("Creates an active card with its own card account in the Ledger.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        cards.MapPost("/{id:guid}/block", BlockCardAsync)
            .WithName("BlockCard")
            .WithSummary("Blocks a card. Blocking a card that is already blocked succeeds.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        cards.MapGet("/{id:guid}", GetCardAsync)
            .WithName("GetCard")
            .WithSummary("Returns a card and its status.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<Results<Created<CardResponse>, ProblemHttpResult>> CreateCardAsync(
        CreateCardRequest request,
        ICommandHandler<CreateCard, CardDto> handler,
        CancellationToken cancellationToken)
    {
        Result<CardDto> result = await handler
            .HandleAsync(new CreateCard(request.Currency), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        return TypedResults.Created($"/cards/{result.Value.Id}", CardResponse.From(result.Value));
    }

    private static async Task<Results<Ok<CardResponse>, ProblemHttpResult>> BlockCardAsync(
        Guid id,
        ICommandHandler<BlockCard, CardDto> handler,
        CancellationToken cancellationToken)
    {
        Result<CardDto> result = await handler
            .HandleAsync(new BlockCard(id), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? result.Error.ToProblem() : TypedResults.Ok(CardResponse.From(result.Value));
    }

    private static async Task<Results<Ok<CardResponse>, ProblemHttpResult>> GetCardAsync(
        Guid id,
        IQueryHandler<GetCard, CardDto> handler,
        CancellationToken cancellationToken)
    {
        Result<CardDto> result = await handler
            .HandleAsync(new GetCard(id), cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? result.Error.ToProblem() : TypedResults.Ok(CardResponse.From(result.Value));
    }
}
