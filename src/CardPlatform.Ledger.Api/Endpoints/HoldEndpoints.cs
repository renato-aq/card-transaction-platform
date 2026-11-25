using CardPlatform.Ledger.Api.Http;
using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Holds;
using CardPlatform.Ledger.Domain.Common;

using Microsoft.AspNetCore.Http.HttpResults;

namespace CardPlatform.Ledger.Api.Endpoints;

internal static class HoldEndpoints
{
    public static IEndpointRouteBuilder MapHoldEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder holds = endpoints.MapGroup("/holds").WithTags("Holds");

        holds.MapPost("/", PlaceHoldAsync)
            .WithName("PlaceHold")
            .WithSummary("Places a hold on a card account, moving the amount from the card to the holds account.")
            .WithDescription(
                "Idempotent by id: repeating the request with the same data returns the existing hold with 200 and " +
                "writes nothing; repeating it with different data returns 409 hold_id_conflict.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        holds.MapGet("/{id:guid}", GetHoldAsync)
            .WithName("GetHold")
            .WithSummary("Returns a hold and its state.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        holds.MapPost("/{id:guid}/capture", CaptureHoldAsync)
            .WithName("CaptureHold")
            .WithSummary("Captures a pending hold, moving its whole amount from the holds account to a merchant account.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        holds.MapPost("/{id:guid}/void", VoidHoldAsync)
            .WithName("VoidHold")
            .WithSummary("Voids a pending hold, returning its whole amount from the holds account to the card.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<Results<Created<HoldResponse>, Ok<HoldResponse>, ProblemHttpResult>> PlaceHoldAsync(
        PlaceHoldRequest request,
        ICommandHandler<PlaceHold, PlaceHoldResult> handler,
        CancellationToken cancellationToken)
    {
        Result<PlaceHoldResult> result = await handler
            .HandleAsync(new PlaceHold(request.Id, request.CardAccountId, request.Amount, request.Currency), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        HoldResponse hold = HoldResponse.From(result.Value.Hold);
        return result.Value.Created
            ? TypedResults.Created($"/holds/{hold.Id}", hold)
            : TypedResults.Ok(hold);
    }

    private static async Task<Results<Ok<HoldResponse>, ProblemHttpResult>> GetHoldAsync(
        Guid id,
        IQueryHandler<GetHold, HoldDto> handler,
        CancellationToken cancellationToken)
    {
        Result<HoldDto> result = await handler.HandleAsync(new GetHold(id), cancellationToken).ConfigureAwait(false);
        return ToResponse(result);
    }

    private static async Task<Results<Ok<HoldResponse>, ProblemHttpResult>> CaptureHoldAsync(
        Guid id,
        CaptureHoldRequest request,
        ICommandHandler<CaptureHold, HoldDto> handler,
        CancellationToken cancellationToken)
    {
        Result<HoldDto> result = await handler
            .HandleAsync(new CaptureHold(id, request.MerchantAccountId), cancellationToken)
            .ConfigureAwait(false);
        return ToResponse(result);
    }

    private static async Task<Results<Ok<HoldResponse>, ProblemHttpResult>> VoidHoldAsync(
        Guid id,
        ICommandHandler<VoidHold, HoldDto> handler,
        CancellationToken cancellationToken)
    {
        Result<HoldDto> result = await handler.HandleAsync(new VoidHold(id), cancellationToken).ConfigureAwait(false);
        return ToResponse(result);
    }

    private static Results<Ok<HoldResponse>, ProblemHttpResult> ToResponse(Result<HoldDto> result)
    {
        return result.IsFailure ? result.Error.ToProblem() : TypedResults.Ok(HoldResponse.From(result.Value));
    }
}
