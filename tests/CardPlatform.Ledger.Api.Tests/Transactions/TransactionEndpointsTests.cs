using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using CardPlatform.Ledger.Api.Tests.Infrastructure;
using CardPlatform.Ledger.Domain.Transactions;
using CardPlatform.Ledger.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Ledger.Api.Tests.Transactions;

/// <summary>
/// The tests share one database, so every test opens its own accounts and only asserts on them.
/// </summary>
[Collection(LedgerApiCollectionFixture.Name)]
public class TransactionEndpointsTests(LedgerApiFactory factory)
{
    private const string ProblemJson = "application/problem+json";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Post_transaction_returns_created_with_the_transaction_and_no_location()
    {
        Guid settlement = await OpenAccountAsync("settlement");
        Guid card = await OpenAccountAsync("card");

        HttpResponseMessage response = await _client.PostAsJsonAsync("/transactions", new
        {
            description = "Top-up",
            currency = "BRL",
            entries = new[]
            {
                new { accountId = settlement, direction = "debit", amount = 100.5m },
                new { accountId = card, direction = "CREDIT", amount = 100.5m },
            },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);
        JsonElement body = await ReadJsonAsync(response);
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal("Top-up", body.GetProperty("description").GetString());
        Assert.Equal("BRL", body.GetProperty("currency").GetString());
        Assert.True(body.GetProperty("createdAt").TryGetDateTimeOffset(out _));
        JsonElement[] entries = [.. body.GetProperty("entries").EnumerateArray()];
        Assert.Equal(2, entries.Length);
        AssertEntry(entries[0], settlement, "debit", 100.5m);
        AssertEntry(entries[1], card, "credit", 100.5m);
    }

    [Fact]
    public async Task Top_up_then_purchase_leaves_the_three_balances_correct()
    {
        Guid settlement = await OpenAccountAsync("settlement");
        Guid card = await OpenAccountAsync("card");
        Guid merchant = await OpenAccountAsync("merchant");

        await PostCreatedAsync("BRL", (settlement, "debit", 100m), (card, "credit", 100m));
        await PostCreatedAsync("BRL", (card, "debit", 30m), (merchant, "credit", 30m));

        Assert.Equal(-100m, await GetBalanceAsync(settlement));
        Assert.Equal(70m, await GetBalanceAsync(card));
        Assert.Equal(30m, await GetBalanceAsync(merchant));
    }

    [Fact]
    public async Task Sum_of_balances_is_zero_after_several_transactions()
    {
        Guid settlement = await OpenAccountAsync("settlement");
        Guid card1 = await OpenAccountAsync("card");
        Guid card2 = await OpenAccountAsync("card");
        Guid merchant = await OpenAccountAsync("merchant");

        await PostCreatedAsync("BRL", (settlement, "debit", 250m), (card1, "credit", 200m), (card2, "credit", 50m));
        await PostCreatedAsync("BRL", (card1, "debit", 120.35m), (merchant, "credit", 120.35m));
        await PostCreatedAsync("BRL", (card2, "debit", 49.99m), (merchant, "credit", 49.99m));
        await PostCreatedAsync("BRL", (merchant, "debit", 100m), (settlement, "credit", 100m));
        await PostRejectedAsync(
            HttpStatusCode.UnprocessableEntity,
            "insufficient_funds",
            "BRL",
            (card2, "debit", 1m),
            (merchant, "credit", 1m));

        decimal[] balances =
        [
            await GetBalanceAsync(settlement),
            await GetBalanceAsync(card1),
            await GetBalanceAsync(card2),
            await GetBalanceAsync(merchant),
        ];
        Assert.Equal([-150m, 79.65m, 0.01m, 70.34m], balances);
        Assert.Equal(0m, balances.Sum());
    }

    [Fact]
    public async Task Purchase_refused_for_insufficient_funds_changes_no_balance_and_saves_no_transaction()
    {
        Guid settlement = await OpenAccountAsync("settlement");
        Guid card = await OpenAccountAsync("card");
        Guid merchant = await OpenAccountAsync("merchant");
        await PostCreatedAsync("BRL", (settlement, "debit", 50m), (card, "credit", 50m));

        await PostRejectedAsync(
            HttpStatusCode.UnprocessableEntity,
            "insufficient_funds",
            "BRL",
            (card, "debit", 50.01m),
            (merchant, "credit", 50.01m));

        Assert.Equal(-50m, await GetBalanceAsync(settlement));
        Assert.Equal(50m, await GetBalanceAsync(card));
        Assert.Equal(0m, await GetBalanceAsync(merchant));
        Assert.Equal(1, await CountEntriesAsync(card));
        Assert.Equal(0, await CountEntriesAsync(merchant));
    }

    [Fact]
    public async Task Unbalanced_transaction_returns_unprocessable_entity_and_saves_nothing()
    {
        Guid settlement = await OpenAccountAsync("settlement");
        Guid card = await OpenAccountAsync("card");

        await PostRejectedAsync(
            HttpStatusCode.UnprocessableEntity,
            "unbalanced_transaction",
            "BRL",
            (settlement, "debit", 100m),
            (card, "credit", 99.99m));

        Assert.Equal(0m, await GetBalanceAsync(settlement));
        Assert.Equal(0m, await GetBalanceAsync(card));
        Assert.Equal(0, await CountEntriesAsync(card));
    }

    [Fact]
    public async Task Account_in_another_currency_returns_unprocessable_entity_and_saves_nothing()
    {
        Guid settlement = await OpenAccountAsync("settlement");
        Guid usdCard = await OpenAccountAsync("card", "USD");

        await PostRejectedAsync(
            HttpStatusCode.UnprocessableEntity,
            "currency_mismatch",
            "BRL",
            (settlement, "debit", 10m),
            (usdCard, "credit", 10m));

        Assert.Equal(0m, await GetBalanceAsync(settlement));
        Assert.Equal(0m, await GetBalanceAsync(usdCard));
        Assert.Equal(0, await CountEntriesAsync(settlement));
    }

    [Fact]
    public async Task Unknown_account_returns_not_found_and_saves_nothing()
    {
        Guid settlement = await OpenAccountAsync("settlement");

        await PostRejectedAsync(
            HttpStatusCode.NotFound,
            "account_not_found",
            "BRL",
            (settlement, "debit", 10m),
            (Guid.CreateVersion7(), "credit", 10m));

        Assert.Equal(0m, await GetBalanceAsync(settlement));
        Assert.Equal(0, await CountEntriesAsync(settlement));
    }

    [Theory]
    [InlineData("BRL", 0, "invalid_amount")]
    [InlineData("BRL", -10, "invalid_amount")]
    [InlineData("BRL", 10.001, "too_many_decimal_places")]
    [InlineData("brl", 10, "invalid_currency")]
    [InlineData("REAL", 10, "invalid_currency")]
    [InlineData(null, 10, "invalid_currency")]
    public async Task Invalid_currency_or_amount_returns_bad_request_with_code(
        string? currency,
        double amount,
        string expectedCode)
    {
        Guid settlement = await OpenAccountAsync("settlement");
        Guid card = await OpenAccountAsync("card");
        decimal value = (decimal)amount;

        await PostRejectedAsync(
            HttpStatusCode.BadRequest,
            expectedCode,
            currency,
            (settlement, "debit", value),
            (card, "credit", value));

        Assert.Equal(0, await CountEntriesAsync(card));
    }

    [Fact]
    public async Task Fewer_than_two_entries_returns_bad_request_with_insufficient_entries()
    {
        Guid card = await OpenAccountAsync("card");

        await PostRejectedAsync(HttpStatusCode.BadRequest, "insufficient_entries", "BRL", (card, "credit", 10m));
        await PostRejectedAsync(HttpStatusCode.BadRequest, "insufficient_entries", "BRL");
        await PostJsonRejectedAsync("""{ "currency": "BRL" }""", "insufficient_entries");

        Assert.Equal(0, await CountEntriesAsync(card));
    }

    [Theory]
    [InlineData("refund")]
    [InlineData("1")]
    [InlineData(null)]
    public async Task Invalid_direction_returns_bad_request_with_code(string? direction)
    {
        await PostRejectedAsync(
            HttpStatusCode.BadRequest,
            "invalid_direction",
            "BRL",
            (Guid.CreateVersion7(), "debit", 10m),
            (Guid.CreateVersion7(), direction, 10m));
    }

    [Theory]
    [InlineData("""{ "currency": "BRL", "entries": [ { "direction": "debit", "amount": 10 }, { "accountId": "0195b5a4-0000-7000-8000-000000000001", "direction": "credit", "amount": 10 } ] }""", "invalid_account_id")]
    [InlineData("""{ "currency": "BRL", "entries": [ { "accountId": "00000000-0000-0000-0000-000000000000", "direction": "debit", "amount": 10 }, { "accountId": "0195b5a4-0000-7000-8000-000000000001", "direction": "credit", "amount": 10 } ] }""", "invalid_account_id")]
    [InlineData("""{ "currency": "BRL", "entries": [ { "accountId": "0195b5a4-0000-7000-8000-000000000001", "direction": "debit" }, { "accountId": "0195b5a4-0000-7000-8000-000000000002", "direction": "credit", "amount": 10 } ] }""", "invalid_amount")]
    [InlineData("""{ "currency": "BRL", "entries": [ null, { "accountId": "0195b5a4-0000-7000-8000-000000000002", "direction": "credit", "amount": 10 } ] }""", "invalid_entry")]
    public async Task Invalid_entry_shape_returns_bad_request_with_code(string json, string expectedCode)
    {
        await PostJsonRejectedAsync(json, expectedCode);
    }

    [Theory]
    [InlineData("{ \"currency\": ")]
    [InlineData("")]
    [InlineData("""{ "currency": "BRL", "entries": [ { "accountId": "not-a-guid", "direction": "debit", "amount": 10 } ] }""")]
    [InlineData("""{ "currency": "BRL", "entries": [ { "accountId": "0195b5a4-0000-7000-8000-000000000001", "direction": 1, "amount": 10 } ] }""")]
    [InlineData("""{ "currency": "BRL", "entries": [ { "accountId": "0195b5a4-0000-7000-8000-000000000001", "direction": "debit", "amount": "ten" } ] }""")]
    public async Task Malformed_body_returns_bad_request_problem(string json)
    {
        using StringContent content = new(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await _client.PostAsync("/transactions", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Input_validation_runs_before_accounts_are_looked_up()
    {
        // Both accounts are unknown, but the unbalanced amounts are reported first.
        await PostRejectedAsync(
            HttpStatusCode.UnprocessableEntity,
            "unbalanced_transaction",
            "BRL",
            (Guid.CreateVersion7(), "debit", 10m),
            (Guid.CreateVersion7(), "credit", 5m));
    }

    [Fact]
    public async Task Openapi_document_describes_the_transaction_endpoint()
    {
        HttpResponseMessage response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement responses = (await ReadJsonAsync(response))
            .GetProperty("paths").GetProperty("/transactions").GetProperty("post").GetProperty("responses");
        foreach (string status in new[] { "201", "400", "404", "409", "422" })
        {
            Assert.True(responses.TryGetProperty(status, out _), $"Missing response {status}.");
        }
    }

    private static void AssertEntry(JsonElement entry, Guid accountId, string direction, decimal amount)
    {
        Assert.Equal(accountId, entry.GetProperty("accountId").GetGuid());
        Assert.Equal(direction, entry.GetProperty("direction").GetString());
        Assert.Equal(JsonValueKind.Number, entry.GetProperty("amount").ValueKind);
        Assert.Equal(amount, entry.GetProperty("amount").GetDecimal());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static object TransactionBody(string? currency, (Guid AccountId, string? Direction, decimal Amount)[] entries)
    {
        return new
        {
            currency,
            entries = entries.Select(static entry => new
            {
                accountId = entry.AccountId,
                direction = entry.Direction,
                amount = entry.Amount,
            }),
        };
    }

    private async Task<Guid> OpenAccountAsync(string type, string currency = "BRL")
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/accounts", new { type, currency });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task PostCreatedAsync(string currency, params (Guid AccountId, string? Direction, decimal Amount)[] entries)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/transactions", TransactionBody(currency, entries));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task PostRejectedAsync(
        HttpStatusCode expectedStatus,
        string expectedCode,
        string? currency,
        params (Guid AccountId, string? Direction, decimal Amount)[] entries)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/transactions", TransactionBody(currency, entries));
        await AssertProblemAsync(response, expectedStatus, expectedCode);
    }

    private async Task PostJsonRejectedAsync(string json, string expectedCode)
    {
        using StringContent content = new(json, Encoding.UTF8, "application/json");
        HttpResponseMessage response = await _client.PostAsync("/transactions", content);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, expectedCode);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
    }

    private async Task<decimal> GetBalanceAsync(Guid accountId)
    {
        HttpResponseMessage response = await _client.GetAsync($"/accounts/{accountId}/balance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("balance").GetProperty("amount").GetDecimal();
    }

    // Reads the database directly: there is no endpoint to list transactions yet.
    private async Task<int> CountEntriesAsync(Guid accountId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        LedgerDbContext dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        return await dbContext.Set<Entry>().CountAsync(entry => entry.AccountId == accountId);
    }
}
