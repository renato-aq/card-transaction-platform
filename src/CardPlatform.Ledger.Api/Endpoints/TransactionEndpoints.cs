using CardPlatform.Ledger.Api.Http;
using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Transactions;
using CardPlatform.Ledger.Domain.Common;

using Microsoft.AspNetCore.Http.HttpResults;

namespace CardPlatform.Ledger.Api.Endpoints;

internal static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder transactions = endpoints.MapGroup("/transactions").WithTags("Transactions");

        transactions.MapPost("/", PostTransactionAsync)
            .WithName("PostTransaction")
            .WithSummary("Posts a double-entry transaction and updates the balances of its accounts atomically.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<Results<Created<TransactionResponse>, ProblemHttpResult>> PostTransactionAsync(
        PostTransactionRequest request,
        ICommandHandler<PostTransaction, LedgerTransactionDto> handler,
        CancellationToken cancellationToken)
    {
        PostTransaction command = new(
            request.Description,
            request.Currency,
            request.Entries?
                .Select(static entry => entry is null ? null : new PostTransactionEntry(entry.AccountId, entry.Direction, entry.Amount))
                .ToList());

        Result<LedgerTransactionDto> result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        // No Location header: there is no endpoint to read a transaction yet.
        return TypedResults.Created((string?)null, TransactionResponse.From(result.Value));
    }
}
