using CardPlatform.Ledger.Api.Http;
using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Accounts;
using CardPlatform.Ledger.Domain.Common;

using Microsoft.AspNetCore.Http.HttpResults;

namespace CardPlatform.Ledger.Api.Endpoints;

internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder accounts = endpoints.MapGroup("/accounts").WithTags("Accounts");

        accounts.MapPost("/", OpenAccountAsync)
            .WithName("OpenAccount")
            .WithSummary("Opens an account with a zero balance.")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        accounts.MapGet("/{id:guid}/balance", GetAccountBalanceAsync)
            .WithName("GetAccountBalance")
            .WithSummary("Returns the current balance of an account.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<Results<Created<AccountResponse>, ProblemHttpResult>> OpenAccountAsync(
        OpenAccountRequest request,
        ICommandHandler<OpenAccount, AccountDto> handler,
        CancellationToken cancellationToken)
    {
        Result<AccountDto> result = await handler
            .HandleAsync(new OpenAccount(request.Type, request.Currency), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        AccountDto account = result.Value;
        return TypedResults.Created(
            $"/accounts/{account.Id}",
            new AccountResponse(account.Id, account.Type, account.Currency, MoneyResponse.From(account.Balance)));
    }

    private static async Task<Results<Ok<AccountBalanceResponse>, ProblemHttpResult>> GetAccountBalanceAsync(
        Guid id,
        IQueryHandler<GetAccountBalance, AccountBalanceDto> handler,
        CancellationToken cancellationToken)
    {
        Result<AccountBalanceDto> result = await handler
            .HandleAsync(new GetAccountBalance(id), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        return TypedResults.Ok(new AccountBalanceResponse(result.Value.AccountId, MoneyResponse.From(result.Value.Balance)));
    }
}
