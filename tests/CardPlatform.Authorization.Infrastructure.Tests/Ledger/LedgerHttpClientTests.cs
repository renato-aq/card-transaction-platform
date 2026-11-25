using System.Net;

using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Infrastructure.Tests.Ledger;

/// <summary>Each operation sends the expected request and translates the Ledger's answers.</summary>
public sealed class LedgerHttpClientTests : IDisposable
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MerchantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid HoldId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CompletedAt = new(2026, 11, 25, 10, 5, 0, TimeSpan.Zero);

    private readonly LedgerClientHarness _ledger = new();

    public void Dispose()
    {
        _ledger.Dispose();
    }

    [Fact]
    public async Task Open_card_account_posts_a_card_account_and_returns_it()
    {
        RespondWith(FakeLedgerHandler.Json(HttpStatusCode.Created, new
        {
            id = AccountId,
            type = "card",
            currency = "BRL",
            balance = new { amount = 0.00m, currency = "BRL" },
        }));

        Result<LedgerAccount> result = await _ledger.Client.OpenCardAccountAsync("BRL");

        RecordedRequest request = Assert.Single(_ledger.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://ledger.test/accounts"), request.Uri);
        Assert.Equal("card", request.Json.GetProperty("type").GetString());
        Assert.Equal("BRL", request.Json.GetProperty("currency").GetString());

        Assert.True(result.IsSuccess);
        Assert.Equal(new LedgerAccount(AccountId, "BRL", 0.00m), result.Value);
    }

    [Fact]
    public async Task Get_balance_gets_the_account_balance()
    {
        RespondWith(FakeLedgerHandler.Json(HttpStatusCode.OK, new
        {
            accountId = AccountId,
            balance = new { amount = 70.50m, currency = "BRL" },
        }));

        Result<LedgerBalance> result = await _ledger.Client.GetBalanceAsync(AccountId);

        RecordedRequest request = Assert.Single(_ledger.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri($"http://ledger.test/accounts/{AccountId}/balance"), request.Uri);

        Assert.True(result.IsSuccess);
        Assert.Equal(new LedgerBalance(AccountId, 70.50m, "BRL"), result.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Place_hold_posts_the_hold_with_its_id_and_returns_it(HttpStatusCode status)
    {
        RespondWith(FakeLedgerHandler.Json(status, HoldBody("pending", completedAt: null)));

        Result<LedgerHold> result = await _ledger.Client.PlaceHoldAsync(HoldId, AccountId, 30.00m, "BRL");

        RecordedRequest request = Assert.Single(_ledger.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("http://ledger.test/holds"), request.Uri);
        Assert.Equal(HoldId, request.Json.GetProperty("id").GetGuid());
        Assert.Equal(AccountId, request.Json.GetProperty("cardAccountId").GetGuid());
        Assert.Equal(30.00m, request.Json.GetProperty("amount").GetDecimal());
        Assert.Equal("BRL", request.Json.GetProperty("currency").GetString());

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new LedgerHold(HoldId, AccountId, 30.00m, "BRL", LedgerHoldStatus.Pending, CreatedAt, null),
            result.Value);
    }

    [Fact]
    public async Task Capture_hold_posts_the_merchant_account_and_returns_the_captured_hold()
    {
        RespondWith(FakeLedgerHandler.Json(HttpStatusCode.OK, HoldBody("captured", CompletedAt)));

        Result<LedgerHold> result = await _ledger.Client.CaptureHoldAsync(HoldId, MerchantId);

        RecordedRequest request = Assert.Single(_ledger.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri($"http://ledger.test/holds/{HoldId}/capture"), request.Uri);
        Assert.Equal(MerchantId, request.Json.GetProperty("merchantAccountId").GetGuid());

        Assert.True(result.IsSuccess);
        Assert.Equal(LedgerHoldStatus.Captured, result.Value.Status);
        Assert.Equal(CompletedAt, result.Value.CompletedAt);
    }

    [Fact]
    public async Task Void_hold_posts_to_the_void_endpoint_and_returns_the_voided_hold()
    {
        RespondWith(FakeLedgerHandler.Json(HttpStatusCode.OK, HoldBody("voided", CompletedAt)));

        Result<LedgerHold> result = await _ledger.Client.VoidHoldAsync(HoldId);

        RecordedRequest request = Assert.Single(_ledger.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri($"http://ledger.test/holds/{HoldId}/void"), request.Uri);
        Assert.Null(request.Body);

        Assert.True(result.IsSuccess);
        Assert.Equal(LedgerHoldStatus.Voided, result.Value.Status);
    }

    [Fact]
    public async Task Get_hold_gets_the_hold_and_its_state()
    {
        RespondWith(FakeLedgerHandler.Json(HttpStatusCode.OK, HoldBody("captured", CompletedAt)));

        Result<LedgerHold> result = await _ledger.Client.GetHoldAsync(HoldId);

        RecordedRequest request = Assert.Single(_ledger.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri($"http://ledger.test/holds/{HoldId}"), request.Uri);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            new LedgerHold(HoldId, AccountId, 30.00m, "BRL", LedgerHoldStatus.Captured, CreatedAt, CompletedAt),
            result.Value);
    }

    public static TheoryData<string, HttpStatusCode, string, ErrorType> LedgerErrorCases => new()
    {
        { "open", HttpStatusCode.BadRequest, "invalid_currency", ErrorType.Validation },
        { "balance", HttpStatusCode.NotFound, "account_not_found", ErrorType.NotFound },
        { "place", HttpStatusCode.BadRequest, "invalid_amount", ErrorType.Validation },
        { "place", HttpStatusCode.BadRequest, "too_many_decimal_places", ErrorType.Validation },
        { "place", HttpStatusCode.BadRequest, "invalid_currency", ErrorType.Validation },
        { "place", HttpStatusCode.NotFound, "account_not_found", ErrorType.NotFound },
        { "place", HttpStatusCode.UnprocessableEntity, "insufficient_funds", ErrorType.BusinessRule },
        { "place", HttpStatusCode.UnprocessableEntity, "currency_mismatch", ErrorType.BusinessRule },
        { "place", HttpStatusCode.UnprocessableEntity, "not_a_card_account", ErrorType.BusinessRule },
        { "place", HttpStatusCode.Conflict, "hold_id_conflict", ErrorType.Conflict },
        { "place", HttpStatusCode.Conflict, "concurrency_conflict", ErrorType.Conflict },
        { "capture", HttpStatusCode.BadRequest, "invalid_account_id", ErrorType.Validation },
        { "capture", HttpStatusCode.NotFound, "hold_not_found", ErrorType.NotFound },
        { "capture", HttpStatusCode.NotFound, "account_not_found", ErrorType.NotFound },
        { "capture", HttpStatusCode.UnprocessableEntity, "hold_not_pending", ErrorType.BusinessRule },
        { "capture", HttpStatusCode.UnprocessableEntity, "not_a_merchant_account", ErrorType.BusinessRule },
        { "capture", HttpStatusCode.UnprocessableEntity, "currency_mismatch", ErrorType.BusinessRule },
        { "capture", HttpStatusCode.Conflict, "concurrency_conflict", ErrorType.Conflict },
        { "void", HttpStatusCode.NotFound, "hold_not_found", ErrorType.NotFound },
        { "void", HttpStatusCode.UnprocessableEntity, "hold_not_pending", ErrorType.BusinessRule },
        { "void", HttpStatusCode.Conflict, "concurrency_conflict", ErrorType.Conflict },
        { "hold", HttpStatusCode.NotFound, "hold_not_found", ErrorType.NotFound },
    };

    [Theory]
    [MemberData(nameof(LedgerErrorCases))]
    public async Task Ledger_errors_are_translated_keeping_the_code_and_are_not_retried(
        string operation,
        HttpStatusCode status,
        string code,
        ErrorType expectedType)
    {
        RespondWith(FakeLedgerHandler.Problem(status, code, "Detail from the Ledger."));

        Result result = await CallAsync(_ledger.Client, operation);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(expectedType, result.Error.Type);
        Assert.Equal("Detail from the Ledger.", result.Error.Message);
        Assert.Single(_ledger.Handler.Requests);
    }

    [Fact]
    public void Known_ledger_codes_match_the_ledger_contract()
    {
        Assert.Equal("insufficient_funds", LedgerErrors.InsufficientFundsCode);
        Assert.Equal("hold_not_pending", LedgerErrors.HoldNotPendingCode);
        Assert.Equal("hold_id_conflict", LedgerErrors.HoldIdConflictCode);
        Assert.Equal("ledger_unavailable", LedgerErrors.UnavailableCode);
        Assert.Equal(ErrorType.Unavailable, LedgerErrors.Unavailable().Type);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Unexpected_ledger_response_throws(HttpStatusCode status)
    {
        // A 404 without a problem code (e.g. a wrong route) or a status the contract does not define is a bug.
        RespondWith(new HttpResponseMessage(status));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _ledger.Client.GetBalanceAsync(AccountId));
    }

    internal static async Task<Result> CallAsync(ILedgerClient client, string operation)
    {
        return operation switch
        {
            "open" => await client.OpenCardAccountAsync("BRL"),
            "balance" => await client.GetBalanceAsync(AccountId),
            "place" => await client.PlaceHoldAsync(HoldId, AccountId, 30.00m, "BRL"),
            "capture" => await client.CaptureHoldAsync(HoldId, MerchantId),
            "void" => await client.VoidHoldAsync(HoldId),
            "hold" => await client.GetHoldAsync(HoldId),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };
    }

    private void RespondWith(HttpResponseMessage response)
    {
        _ledger.Handler.Respond = (_, _, _) => Task.FromResult(response);
    }

    private static object HoldBody(string status, DateTimeOffset? completedAt)
    {
        return new
        {
            id = HoldId,
            cardAccountId = AccountId,
            amount = 30.00m,
            currency = "BRL",
            status,
            createdAt = CreatedAt,
            completedAt,
        };
    }
}
