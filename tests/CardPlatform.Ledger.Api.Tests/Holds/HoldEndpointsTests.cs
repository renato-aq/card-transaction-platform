using System.Net;
using System.Text;
using System.Text.Json;

using CardPlatform.Ledger.Api.Tests.Infrastructure;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Concurrency;
using CardPlatform.Ledger.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Ledger.Api.Tests.Holds;

/// <summary>
/// The tests share one database, so every test opens its own accounts and only asserts on them. Tests that check the
/// balance of the holds account use a currency of their own, so that account (one per currency) is theirs alone.
/// </summary>
[Collection(LedgerApiCollectionFixture.Name)]
public partial class HoldEndpointsTests(LedgerApiFactory factory)
{
    private readonly HoldApi _api = new(factory, factory.CreateClient());

    [Fact]
    public async Task Place_hold_returns_created_with_the_pending_hold_and_its_location()
    {
        (_, Guid card) = await _api.OpenFundedCardAsync(100m);
        Guid id = Guid.CreateVersion7();

        HttpResponseMessage response = await _api.PlaceAsync(id, card, 30.5m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/holds/{id}", response.Headers.Location?.OriginalString);
        HoldApi.AssertHold(await HoldApi.ReadJsonAsync(response), id, card, 30.5m, "BRL", "pending");
        HoldApi.AssertHold(await _api.GetHoldAsync(id), id, card, 30.5m, "BRL", "pending");
    }

    [Fact]
    public async Task Top_up_hold_and_capture_leave_the_card_holds_and_merchant_balances_correct()
    {
        const string currency = "HCA";
        (Guid settlement, Guid card) = await _api.OpenFundedCardAsync(100m, currency);
        Guid merchant = await _api.OpenAccountAsync("merchant", currency);

        Guid id = await _api.PlaceCreatedAsync(card, 30m, currency);
        Guid holds = await _api.GetHoldsAccountAsync(currency);
        Assert.Equal(70m, await _api.GetBalanceAsync(card));
        Assert.Equal(30m, await _api.GetBalanceAsync(holds));
        Assert.Equal(0m, await _api.GetBalanceAsync(merchant));

        HttpResponseMessage response = await _api.CaptureAsync(id, merchant);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HoldApi.AssertHold(await HoldApi.ReadJsonAsync(response), id, card, 30m, currency, "captured");
        HoldApi.AssertHold(await _api.GetHoldAsync(id), id, card, 30m, currency, "captured");
        decimal[] balances =
        [
            await _api.GetBalanceAsync(settlement),
            await _api.GetBalanceAsync(card),
            await _api.GetBalanceAsync(holds),
            await _api.GetBalanceAsync(merchant),
        ];
        Assert.Equal([-100m, 70m, 0m, 30m], balances);
        Assert.Equal(0m, balances.Sum());
    }

    [Fact]
    public async Task Hold_then_void_returns_the_balance_to_the_card()
    {
        const string currency = "HVO";
        (Guid settlement, Guid card) = await _api.OpenFundedCardAsync(100m, currency);

        Guid id = await _api.PlaceCreatedAsync(card, 40m, currency);
        Guid holds = await _api.GetHoldsAccountAsync(currency);
        Assert.Equal(60m, await _api.GetBalanceAsync(card));
        Assert.Equal(40m, await _api.GetBalanceAsync(holds));

        HttpResponseMessage response = await _api.VoidAsync(id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HoldApi.AssertHold(await HoldApi.ReadJsonAsync(response), id, card, 40m, currency, "voided");
        HoldApi.AssertHold(await _api.GetHoldAsync(id), id, card, 40m, currency, "voided");
        Assert.Equal(-100m, await _api.GetBalanceAsync(settlement));
        Assert.Equal(100m, await _api.GetBalanceAsync(card));
        Assert.Equal(0m, await _api.GetBalanceAsync(holds));
    }

    [Fact]
    public async Task Repeating_the_same_place_returns_the_existing_hold_without_a_new_transaction()
    {
        (_, Guid card) = await _api.OpenFundedCardAsync(100m);
        Guid id = Guid.CreateVersion7();
        HttpResponseMessage first = await _api.PlaceAsync(id, card, 25m);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // 25.00 is the same amount as 25.
        HttpResponseMessage second = await _api.PlaceAsync(id, card, 25.00m);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Null(second.Headers.Location);
        HoldApi.AssertHold(await HoldApi.ReadJsonAsync(second), id, card, 25m, "BRL", "pending");
        Assert.Equal(75m, await _api.GetBalanceAsync(card));
        Assert.Equal(2, await _api.CountEntriesAsync(card));
        Assert.Equal(1, await _api.CountHoldsAsync(card));
    }

    [Fact]
    public async Task Repeating_a_completed_place_returns_the_hold_in_its_current_state()
    {
        (_, Guid card) = await _api.OpenFundedCardAsync(100m);
        Guid id = await _api.PlaceCreatedAsync(card, 25m);
        Assert.Equal(HttpStatusCode.OK, (await _api.VoidAsync(id)).StatusCode);

        HttpResponseMessage response = await _api.PlaceAsync(id, card, 25m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HoldApi.AssertHold(await HoldApi.ReadJsonAsync(response), id, card, 25m, "BRL", "voided");
        Assert.Equal(100m, await _api.GetBalanceAsync(card));
        Assert.Equal(3, await _api.CountEntriesAsync(card));
    }

    [Fact]
    public async Task Repeating_the_place_with_different_data_returns_conflict_and_writes_nothing()
    {
        (_, Guid card) = await _api.OpenFundedCardAsync(100m);
        (_, Guid otherCard) = await _api.OpenFundedCardAsync(100m);
        Guid id = await _api.PlaceCreatedAsync(card, 25m);

        await HoldApi.AssertProblemAsync(await _api.PlaceAsync(id, card, 26m), HttpStatusCode.Conflict, "hold_id_conflict");
        await HoldApi.AssertProblemAsync(
            await _api.PlaceAsync(id, otherCard, 25m),
            HttpStatusCode.Conflict,
            "hold_id_conflict");
        await HoldApi.AssertProblemAsync(
            await _api.PlaceAsync(id, card, 25m, "USD"),
            HttpStatusCode.Conflict,
            "hold_id_conflict");

        Assert.Equal(75m, await _api.GetBalanceAsync(card));
        Assert.Equal(100m, await _api.GetBalanceAsync(otherCard));
        Assert.Equal(2, await _api.CountEntriesAsync(card));
        Assert.Equal(1, await _api.CountEntriesAsync(otherCard));
        HoldApi.AssertHold(await _api.GetHoldAsync(id), id, card, 25m, "BRL", "pending");
    }

    [Theory]
    [InlineData("""{ "cardAccountId": "0195b5a4-0000-7000-8000-000000000001", "amount": 10, "currency": "BRL" }""", "invalid_hold_id")]
    [InlineData("""{ "id": "00000000-0000-0000-0000-000000000000", "cardAccountId": "0195b5a4-0000-7000-8000-000000000001", "amount": 10, "currency": "BRL" }""", "invalid_hold_id")]
    [InlineData("""{ "id": "0195b5a4-0000-7000-8000-0000000000a1", "amount": 10, "currency": "BRL" }""", "invalid_account_id")]
    [InlineData("""{ "id": "0195b5a4-0000-7000-8000-0000000000a1", "cardAccountId": "00000000-0000-0000-0000-000000000000", "amount": 10, "currency": "BRL" }""", "invalid_account_id")]
    [InlineData("""{ "id": "0195b5a4-0000-7000-8000-0000000000a1", "cardAccountId": "0195b5a4-0000-7000-8000-000000000001", "amount": 10 }""", "invalid_currency")]
    [InlineData("""{ "id": "0195b5a4-0000-7000-8000-0000000000a1", "cardAccountId": "0195b5a4-0000-7000-8000-000000000001", "amount": 10, "currency": "brl" }""", "invalid_currency")]
    [InlineData("""{ "id": "0195b5a4-0000-7000-8000-0000000000a1", "cardAccountId": "0195b5a4-0000-7000-8000-000000000001", "currency": "BRL" }""", "invalid_amount")]
    [InlineData("""{ "id": "0195b5a4-0000-7000-8000-0000000000a1", "cardAccountId": "0195b5a4-0000-7000-8000-000000000001", "amount": 0, "currency": "BRL" }""", "invalid_amount")]
    [InlineData("""{ "id": "0195b5a4-0000-7000-8000-0000000000a1", "cardAccountId": "0195b5a4-0000-7000-8000-000000000001", "amount": -5, "currency": "BRL" }""", "invalid_amount")]
    [InlineData("""{ "id": "0195b5a4-0000-7000-8000-0000000000a1", "cardAccountId": "0195b5a4-0000-7000-8000-000000000001", "amount": 10.001, "currency": "BRL" }""", "too_many_decimal_places")]
    public async Task Invalid_place_returns_bad_request_with_code(string json, string expectedCode)
    {
        using StringContent content = new(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _api.Client.PostAsync("/holds", content);

        await HoldApi.AssertProblemAsync(response, HttpStatusCode.BadRequest, expectedCode);
    }

    [Theory]
    [InlineData("{ \"id\": ")]
    [InlineData("")]
    [InlineData("""{ "id": "not-a-guid", "cardAccountId": "0195b5a4-0000-7000-8000-000000000001", "amount": 10, "currency": "BRL" }""")]
    [InlineData("""{ "id": "0195b5a4-0000-7000-8000-0000000000a1", "cardAccountId": "0195b5a4-0000-7000-8000-000000000001", "amount": "ten", "currency": "BRL" }""")]
    public async Task Malformed_place_body_returns_bad_request_problem(string json)
    {
        using StringContent content = new(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _api.Client.PostAsync("/holds", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HoldApi.ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Capture_without_merchant_account_returns_bad_request()
    {
        (_, Guid card) = await _api.OpenFundedCardAsync(100m);
        Guid id = await _api.PlaceCreatedAsync(card, 10m);

        await HoldApi.AssertProblemAsync(await _api.CaptureAsync(id, null), HttpStatusCode.BadRequest, "invalid_account_id");
        await HoldApi.AssertProblemAsync(
            await _api.CaptureAsync(id, Guid.Empty),
            HttpStatusCode.BadRequest,
            "invalid_account_id");
        HoldApi.AssertHold(await _api.GetHoldAsync(id), id, card, 10m, "BRL", "pending");
    }

    [Fact]
    public async Task Unknown_hold_returns_not_found()
    {
        Guid merchant = await _api.OpenAccountAsync("merchant");
        Guid unknown = Guid.CreateVersion7();

        await HoldApi.AssertProblemAsync(
            await _api.Client.GetAsync($"/holds/{unknown}"),
            HttpStatusCode.NotFound,
            "hold_not_found");
        await HoldApi.AssertProblemAsync(await _api.CaptureAsync(unknown, merchant), HttpStatusCode.NotFound, "hold_not_found");
        await HoldApi.AssertProblemAsync(await _api.VoidAsync(unknown), HttpStatusCode.NotFound, "hold_not_found");
    }

    [Fact]
    public async Task Unknown_card_or_merchant_account_returns_not_found_and_writes_nothing()
    {
        (_, Guid card) = await _api.OpenFundedCardAsync(100m);
        Guid id = await _api.PlaceCreatedAsync(card, 10m);

        await HoldApi.AssertProblemAsync(
            await _api.PlaceAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), 10m),
            HttpStatusCode.NotFound,
            "account_not_found");
        await HoldApi.AssertProblemAsync(
            await _api.CaptureAsync(id, Guid.CreateVersion7()),
            HttpStatusCode.NotFound,
            "account_not_found");

        HoldApi.AssertHold(await _api.GetHoldAsync(id), id, card, 10m, "BRL", "pending");
        Assert.Equal(90m, await _api.GetBalanceAsync(card));
    }

    [Fact]
    public async Task Hold_above_the_card_balance_returns_insufficient_funds_and_writes_nothing()
    {
        (_, Guid card) = await _api.OpenFundedCardAsync(50m);
        Guid id = Guid.CreateVersion7();

        await HoldApi.AssertProblemAsync(
            await _api.PlaceAsync(id, card, 50.01m),
            HttpStatusCode.UnprocessableEntity,
            "insufficient_funds");

        Assert.Equal(50m, await _api.GetBalanceAsync(card));
        Assert.Equal(1, await _api.CountEntriesAsync(card));
        Assert.Equal(0, await _api.CountHoldsAsync(card));
        await HoldApi.AssertProblemAsync(await _api.Client.GetAsync($"/holds/{id}"), HttpStatusCode.NotFound, "hold_not_found");
    }

    [Fact]
    public async Task Capturing_or_voiding_a_completed_hold_returns_hold_not_pending()
    {
        (_, Guid card) = await _api.OpenFundedCardAsync(100m);
        Guid merchant = await _api.OpenAccountAsync("merchant");
        Guid captured = await _api.PlaceCreatedAsync(card, 10m);
        Guid voided = await _api.PlaceCreatedAsync(card, 20m);
        Assert.Equal(HttpStatusCode.OK, (await _api.CaptureAsync(captured, merchant)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.VoidAsync(voided)).StatusCode);

        await HoldApi.AssertProblemAsync(
            await _api.CaptureAsync(captured, merchant),
            HttpStatusCode.UnprocessableEntity,
            "hold_not_pending");
        await HoldApi.AssertProblemAsync(await _api.VoidAsync(captured), HttpStatusCode.UnprocessableEntity, "hold_not_pending");
        await HoldApi.AssertProblemAsync(
            await _api.CaptureAsync(voided, merchant),
            HttpStatusCode.UnprocessableEntity,
            "hold_not_pending");
        await HoldApi.AssertProblemAsync(await _api.VoidAsync(voided), HttpStatusCode.UnprocessableEntity, "hold_not_pending");

        Assert.Equal(90m, await _api.GetBalanceAsync(card));
        Assert.Equal(10m, await _api.GetBalanceAsync(merchant));
    }

    [Fact]
    public async Task Accounts_in_another_currency_return_currency_mismatch_and_write_nothing()
    {
        (_, Guid card) = await _api.OpenFundedCardAsync(100m);
        Guid usdMerchant = await _api.OpenAccountAsync("merchant", "USD");
        Guid id = await _api.PlaceCreatedAsync(card, 10m);

        await HoldApi.AssertProblemAsync(
            await _api.PlaceAsync(Guid.CreateVersion7(), card, 10m, "USD"),
            HttpStatusCode.UnprocessableEntity,
            "currency_mismatch");
        await HoldApi.AssertProblemAsync(
            await _api.CaptureAsync(id, usdMerchant),
            HttpStatusCode.UnprocessableEntity,
            "currency_mismatch");

        Assert.Equal(90m, await _api.GetBalanceAsync(card));
        Assert.Equal(0m, await _api.GetBalanceAsync(usdMerchant));
        HoldApi.AssertHold(await _api.GetHoldAsync(id), id, card, 10m, "BRL", "pending");
    }

    [Fact]
    public async Task Accounts_of_the_wrong_type_return_unprocessable_entity()
    {
        (Guid settlement, Guid card) = await _api.OpenFundedCardAsync(100m);
        Guid merchant = await _api.OpenAccountAsync("merchant");
        Guid id = await _api.PlaceCreatedAsync(card, 10m);

        await HoldApi.AssertProblemAsync(
            await _api.PlaceAsync(Guid.CreateVersion7(), merchant, 10m),
            HttpStatusCode.UnprocessableEntity,
            "not_a_card_account");
        await HoldApi.AssertProblemAsync(
            await _api.CaptureAsync(id, settlement),
            HttpStatusCode.UnprocessableEntity,
            "not_a_merchant_account");
        await HoldApi.AssertProblemAsync(
            await _api.CaptureAsync(id, card),
            HttpStatusCode.UnprocessableEntity,
            "not_a_merchant_account");

        Assert.Equal(90m, await _api.GetBalanceAsync(card));
        Assert.Equal(0m, await _api.GetBalanceAsync(merchant));
    }

    [Fact]
    public async Task Hold_commands_that_keep_conflicting_return_concurrency_conflict_and_write_nothing()
    {
        (_, Guid card) = await _api.OpenFundedCardAsync(100m);
        Guid merchant = await _api.OpenAccountAsync("merchant");
        Guid pending = await _api.PlaceCreatedAsync(card, 10m);
        Guid newHold = Guid.CreateVersion7();

        // Every save conflicts, as if other requests always changed the accounts first; the retry decorator gives up.
        await using WebApplicationFactory<Program> conflicting = factory.WithWebHostBuilder(static builder =>
            builder.ConfigureServices(static services =>
            {
                services.AddScoped<IUnitOfWork>(static provider =>
                    new AlwaysConflictingUnitOfWork(provider.GetRequiredService<LedgerDbContext>()));
                services.AddSingleton<IRetryDelay, NoRetryDelay>();
            }));
        HoldApi api = new(factory, conflicting.CreateClient());

        await HoldApi.AssertProblemAsync(
            await api.PlaceAsync(newHold, card, 10m),
            HttpStatusCode.Conflict,
            "concurrency_conflict");
        await HoldApi.AssertProblemAsync(
            await api.CaptureAsync(pending, merchant),
            HttpStatusCode.Conflict,
            "concurrency_conflict");
        await HoldApi.AssertProblemAsync(await api.VoidAsync(pending), HttpStatusCode.Conflict, "concurrency_conflict");

        await HoldApi.AssertProblemAsync(
            await _api.Client.GetAsync($"/holds/{newHold}"),
            HttpStatusCode.NotFound,
            "hold_not_found");
        HoldApi.AssertHold(await _api.GetHoldAsync(pending), pending, card, 10m, "BRL", "pending");
        Assert.Equal(90m, await _api.GetBalanceAsync(card));
        Assert.Equal(0m, await _api.GetBalanceAsync(merchant));
    }

    [Fact]
    public async Task Openapi_document_describes_the_hold_endpoints()
    {
        HttpResponseMessage response = await _api.Client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement paths = (await HoldApi.ReadJsonAsync(response)).GetProperty("paths");
        AssertResponses(paths, "/holds", "post", "200", "201", "400", "404", "409", "422");
        AssertResponses(paths, "/holds/{id}", "get", "200", "404");
        AssertResponses(paths, "/holds/{id}/capture", "post", "200", "400", "404", "409", "422");
        AssertResponses(paths, "/holds/{id}/void", "post", "200", "404", "409", "422");
    }

    private static void AssertResponses(JsonElement paths, string path, string method, params string[] statuses)
    {
        Assert.True(paths.TryGetProperty(path, out JsonElement item), $"Missing path {path}.");
        JsonElement responses = item.GetProperty(method).GetProperty("responses");
        foreach (string status in statuses)
        {
            Assert.True(responses.TryGetProperty(status, out _), $"Missing response {status} of {method} {path}.");
        }
    }

    private sealed class AlwaysConflictingUnitOfWork(IUnitOfWork inner) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new ConcurrencyConflictException();
        }

        public void DiscardChanges()
        {
            inner.DiscardChanges();
        }
    }

    private sealed class NoRetryDelay : IRetryDelay
    {
        public Task WaitAsync(int retry, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
