using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using CardPlatform.Ledger.Api.Tests.Infrastructure;

namespace CardPlatform.Ledger.Api.Tests.Accounts;

[Collection(LedgerApiCollectionFixture.Name)]
public class AccountEndpointsTests(LedgerApiFactory factory)
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task Post_accounts_with_valid_body_returns_created_with_location_and_zero_balance()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/accounts", new { type = "card", currency = "BRL" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await ReadJsonAsync(response);
        Guid id = body.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal($"/accounts/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("card", body.GetProperty("type").GetString());
        Assert.Equal("BRL", body.GetProperty("currency").GetString());
        AssertZeroBalance(body.GetProperty("balance"), "BRL");
    }

    [Theory]
    [InlineData("Merchant", "merchant")]
    [InlineData("SETTLEMENT", "settlement")]
    public async Task Post_accounts_accepts_account_type_in_any_case(string type, string expectedType)
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/accounts", new { type, currency = "USD" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement body = await ReadJsonAsync(response);
        Assert.Equal(expectedType, body.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("savings", "BRL", "invalid_account_type")]
    [InlineData("1", "BRL", "invalid_account_type")]
    [InlineData(null, "BRL", "invalid_account_type")]
    [InlineData("card", "brl", "invalid_currency")]
    [InlineData("card", "REAL", "invalid_currency")]
    [InlineData("card", null, "invalid_currency")]
    public async Task Post_accounts_with_invalid_type_or_currency_returns_bad_request_with_code(
        string? type,
        string? currency,
        string expectedCode)
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/accounts", new { type, currency });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("{ \"type\": ")]
    [InlineData("")]
    public async Task Post_accounts_with_malformed_body_returns_bad_request_problem(string json)
    {
        HttpClient client = factory.CreateClient();
        using StringContent content = new(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync("/accounts", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_balance_of_new_account_returns_zero()
    {
        HttpClient client = factory.CreateClient();
        HttpResponseMessage created = await client.PostAsJsonAsync("/accounts", new { type = "merchant", currency = "EUR" });
        Guid id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        HttpResponseMessage response = await client.GetAsync($"/accounts/{id}/balance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ReadJsonAsync(response);
        Assert.Equal(id, body.GetProperty("accountId").GetGuid());
        AssertZeroBalance(body.GetProperty("balance"), "EUR");
    }

    [Fact]
    public async Task Get_balance_of_unknown_account_returns_not_found_with_code()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/accounts/{Guid.NewGuid()}/balance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("account_not_found", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Openapi_document_describes_account_endpoints()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement paths = (await ReadJsonAsync(response)).GetProperty("paths");
        JsonElement post = paths.GetProperty("/accounts").GetProperty("post");
        Assert.True(post.GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(post.GetProperty("responses").TryGetProperty("400", out _));
        JsonElement get = paths.GetProperty("/accounts/{id}/balance").GetProperty("get");
        Assert.True(get.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(get.GetProperty("responses").TryGetProperty("404", out _));
    }

    private static void AssertZeroBalance(JsonElement balance, string currency)
    {
        Assert.Equal(JsonValueKind.Number, balance.GetProperty("amount").ValueKind);
        Assert.Equal(0m, balance.GetProperty("amount").GetDecimal());
        Assert.Equal(currency, balance.GetProperty("currency").GetString());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
