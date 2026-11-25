using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using CardPlatform.Authorization.Api.Tests.Infrastructure;
using CardPlatform.Authorization.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Authorization.Api.Tests.Authorizations;

[Collection(AuthorizationApiCollectionFixture.Name)]
public class AuthorizationEndpointsTests(AuthorizationApiFactory factory)
{
    private const string ProblemJson = "application/problem+json";

    private readonly HttpClient _client = factory.CreateClient();
    private readonly HttpClient _ledger = factory.Ledger.CreateClient();

    [Fact]
    public async Task Purchase_with_balance_is_approved_and_the_card_balance_in_the_ledger_drops_by_the_amount()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        Guid merchantAccountId = await OpenLedgerAccountAsync("merchant");

        HttpResponseMessage response = await AuthorizeAsync(cardId, merchantAccountId, 30m, "BRL");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement authorization = await ReadJsonAsync(response);
        Guid id = authorization.GetProperty("id").GetGuid();
        Assert.Equal($"/authorizations/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("approved", authorization.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, authorization.GetProperty("declineReason").ValueKind);
        Assert.Equal(cardId, authorization.GetProperty("cardId").GetGuid());
        Assert.Equal(merchantAccountId, authorization.GetProperty("merchantAccountId").GetGuid());
        Assert.Equal(30m, authorization.GetProperty("amount").GetDecimal());
        Assert.Equal("BRL", authorization.GetProperty("currency").GetString());
        Assert.NotEqual(default, authorization.GetProperty("createdAt").GetDateTimeOffset());

        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));

        // The hold was placed in the Ledger with the authorization id as its id.
        HttpResponseMessage holdResponse = await _ledger.GetAsync($"/holds/{id}");
        Assert.Equal(HttpStatusCode.OK, holdResponse.StatusCode);
        JsonElement hold = await ReadJsonAsync(holdResponse);
        Assert.Equal(cardAccountId, hold.GetProperty("cardAccountId").GetGuid());
        Assert.Equal(30m, hold.GetProperty("amount").GetDecimal());
        Assert.Equal("pending", hold.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_authorization_returns_the_recorded_authorization()
    {
        (Guid cardId, _) = await CreateFundedCardAsync(100m);
        Guid merchantAccountId = Guid.NewGuid();
        JsonElement created = await ReadJsonAsync(await AuthorizeAsync(cardId, merchantAccountId, 12.5m, "BRL"));
        Guid id = created.GetProperty("id").GetGuid();

        HttpResponseMessage response = await _client.GetAsync($"/authorizations/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement authorization = await ReadJsonAsync(response);
        Assert.Equal(id, authorization.GetProperty("id").GetGuid());
        Assert.Equal("approved", authorization.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, authorization.GetProperty("declineReason").ValueKind);
        Assert.Equal(cardId, authorization.GetProperty("cardId").GetGuid());
        Assert.Equal(merchantAccountId, authorization.GetProperty("merchantAccountId").GetGuid());
        Assert.Equal(12.5m, authorization.GetProperty("amount").GetDecimal());
        Assert.Equal("BRL", authorization.GetProperty("currency").GetString());
        Assert.Equal(
            created.GetProperty("createdAt").GetDateTimeOffset(),
            authorization.GetProperty("createdAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Blocked_card_is_declined_with_card_blocked_and_the_balance_is_unchanged()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/cards/{cardId}/block", content: null)).StatusCode);

        HttpResponseMessage response = await AuthorizeAsync(cardId, Guid.NewGuid(), 30m, "BRL");

        Guid id = await AssertDeclinedAsync(response, "card_blocked");
        Assert.Equal(100m, await GetLedgerBalanceAsync(cardAccountId));
        Assert.Equal(HttpStatusCode.NotFound, (await _ledger.GetAsync($"/holds/{id}")).StatusCode);
        await AssertRecordedAsync(id, "declined", "card_blocked");
    }

    [Fact]
    public async Task Purchase_above_the_balance_is_declined_with_insufficient_funds()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(20m);

        HttpResponseMessage response = await AuthorizeAsync(cardId, Guid.NewGuid(), 30m, "BRL");

        Guid id = await AssertDeclinedAsync(response, "insufficient_funds");
        Assert.Equal(20m, await GetLedgerBalanceAsync(cardAccountId));
        await AssertRecordedAsync(id, "declined", "insufficient_funds");
    }

    [Fact]
    public async Task Purchase_in_another_currency_is_declined_with_currency_mismatch()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);

        HttpResponseMessage response = await AuthorizeAsync(cardId, Guid.NewGuid(), 30m, "USD");

        Guid id = await AssertDeclinedAsync(response, "currency_mismatch");
        Assert.Equal(100m, await GetLedgerBalanceAsync(cardAccountId));
        await AssertRecordedAsync(id, "declined", "currency_mismatch");
    }

    [Theory]
    [InlineData("0", "BRL", "invalid_amount")]
    [InlineData("-10", "BRL", "invalid_amount")]
    [InlineData("10.001", "BRL", "too_many_decimal_places")]
    [InlineData("10", "brl", "invalid_currency")]
    [InlineData("10", "REAL", "invalid_currency")]
    public async Task Invalid_amount_or_currency_returns_bad_request_with_code(string amount, string currency, string code)
    {
        (Guid cardId, _) = await CreateFundedCardAsync(100m);
        int before = await CountAuthorizationsAsync();

        HttpResponseMessage response = await _client.PostAsJsonAsync("/authorizations", new
        {
            cardId,
            merchantAccountId = Guid.NewGuid(),
            amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture),
            currency,
        });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, code);
        Assert.Equal(before, await CountAuthorizationsAsync());
    }

    [Fact]
    public async Task Missing_ids_return_bad_request_with_code()
    {
        HttpResponseMessage noCard = await _client.PostAsJsonAsync(
            "/authorizations",
            new { merchantAccountId = Guid.NewGuid(), amount = 10m, currency = "BRL" });
        HttpResponseMessage noMerchant = await _client.PostAsJsonAsync(
            "/authorizations",
            new { cardId = Guid.NewGuid(), amount = 10m, currency = "BRL" });

        await AssertProblemAsync(noCard, HttpStatusCode.BadRequest, "invalid_card_id");
        await AssertProblemAsync(noMerchant, HttpStatusCode.BadRequest, "invalid_merchant_account_id");
    }

    [Theory]
    [InlineData("{ \"cardId\": ")]
    [InlineData("{ \"cardId\": \"not-a-guid\", \"amount\": 10, \"currency\": \"BRL\" }")]
    [InlineData("{ \"cardId\": \"0196f1a4-0000-7000-8000-000000000000\", \"amount\": \"ten\", \"currency\": \"BRL\" }")]
    [InlineData("")]
    public async Task Malformed_body_returns_bad_request_problem(string json)
    {
        using StringContent content = new(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _client.PostAsync("/authorizations", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_card_returns_not_found_with_card_not_found()
    {
        int before = await CountAuthorizationsAsync();

        HttpResponseMessage response = await AuthorizeAsync(Guid.NewGuid(), Guid.NewGuid(), 10m, "BRL");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "card_not_found");
        Assert.Equal(before, await CountAuthorizationsAsync());
    }

    [Fact]
    public async Task Get_unknown_authorization_returns_not_found_with_code()
    {
        HttpResponseMessage response = await _client.GetAsync($"/authorizations/{Guid.NewGuid()}");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "authorization_not_found");
    }

    [Fact]
    public async Task Ledger_down_returns_service_unavailable_and_records_nothing()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        int before = await CountAuthorizationsAsync();
        using WebApplicationFactory<Program> ledgerDown = factory.WithLedgerDown();

        HttpResponseMessage response = await ledgerDown.CreateClient()
            .PostAsJsonAsync("/authorizations", new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" });

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "ledger_unavailable");
        Assert.Equal(before, await CountAuthorizationsAsync());
        Assert.Equal(100m, await GetLedgerBalanceAsync(cardAccountId));
    }

    private Task<HttpResponseMessage> AuthorizeAsync(Guid cardId, Guid merchantAccountId, decimal amount, string currency)
    {
        return _client.PostAsJsonAsync("/authorizations", new { cardId, merchantAccountId, amount, currency });
    }

    /// <summary>Creates a BRL card and tops its Ledger card account up from a settlement account.</summary>
    private async Task<(Guid CardId, Guid CardAccountId)> CreateFundedCardAsync(decimal balance)
    {
        HttpResponseMessage cardResponse = await _client.PostAsJsonAsync("/cards", new { currency = "BRL" });
        Assert.Equal(HttpStatusCode.Created, cardResponse.StatusCode);
        JsonElement card = await ReadJsonAsync(cardResponse);
        Guid cardAccountId = card.GetProperty("ledgerAccountId").GetGuid();

        Guid settlement = await OpenLedgerAccountAsync("settlement");
        HttpResponseMessage topUp = await _ledger.PostAsJsonAsync("/transactions", new
        {
            currency = "BRL",
            entries = new[]
            {
                new { accountId = settlement, direction = "debit", amount = balance },
                new { accountId = cardAccountId, direction = "credit", amount = balance },
            },
        });
        Assert.Equal(HttpStatusCode.Created, topUp.StatusCode);

        return (card.GetProperty("id").GetGuid(), cardAccountId);
    }

    private async Task<Guid> OpenLedgerAccountAsync(string type)
    {
        HttpResponseMessage response = await _ledger.PostAsJsonAsync("/accounts", new { type, currency = "BRL" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<decimal> GetLedgerBalanceAsync(Guid accountId)
    {
        HttpResponseMessage response = await _ledger.GetAsync($"/accounts/{accountId}/balance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("balance").GetProperty("amount").GetDecimal();
    }

    private static async Task<Guid> AssertDeclinedAsync(HttpResponseMessage response, string reason)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement authorization = await ReadJsonAsync(response);
        Guid id = authorization.GetProperty("id").GetGuid();
        Assert.Equal($"/authorizations/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("declined", authorization.GetProperty("status").GetString());
        Assert.Equal(reason, authorization.GetProperty("declineReason").GetString());
        return id;
    }

    private async Task AssertRecordedAsync(Guid id, string status, string? reason)
    {
        HttpResponseMessage response = await _client.GetAsync($"/authorizations/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement authorization = await ReadJsonAsync(response);
        Assert.Equal(status, authorization.GetProperty("status").GetString());
        Assert.Equal(reason, authorization.GetProperty("declineReason").GetString());
    }

    private async Task<int> CountAuthorizationsAsync()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        return await dbContext.Authorizations.CountAsync();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
