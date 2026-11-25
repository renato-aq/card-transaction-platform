using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using CardPlatform.Authorization.Api.Tests.Infrastructure;
using CardPlatform.Authorization.Infrastructure.Persistence;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Authorization.Api.Tests.Cards;

[Collection(AuthorizationApiCollectionFixture.Name)]
public class CardEndpointsTests(AuthorizationApiFactory factory)
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task Post_cards_creates_an_active_card_whose_account_exists_in_the_ledger()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/cards", new { currency = "BRL" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement card = await ReadJsonAsync(response);
        Guid id = card.GetProperty("id").GetGuid();
        Guid ledgerAccountId = card.GetProperty("ledgerAccountId").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.NotEqual(Guid.Empty, ledgerAccountId);
        Assert.Equal($"/cards/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("BRL", card.GetProperty("currency").GetString());
        Assert.Equal("active", card.GetProperty("status").GetString());

        // The card account was really opened in the Ledger, empty and in the card currency.
        HttpResponseMessage balanceResponse = await factory.Ledger.CreateClient()
            .GetAsync($"/accounts/{ledgerAccountId}/balance");
        Assert.Equal(HttpStatusCode.OK, balanceResponse.StatusCode);
        JsonElement balance = (await ReadJsonAsync(balanceResponse)).GetProperty("balance");
        Assert.Equal(0m, balance.GetProperty("amount").GetDecimal());
        Assert.Equal("BRL", balance.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Get_card_returns_the_created_card()
    {
        HttpClient client = factory.CreateClient();
        JsonElement created = await CreateCardAsync(client, "USD");
        Guid id = created.GetProperty("id").GetGuid();

        HttpResponseMessage response = await client.GetAsync($"/cards/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement card = await ReadJsonAsync(response);
        Assert.Equal(id, card.GetProperty("id").GetGuid());
        Assert.Equal(created.GetProperty("ledgerAccountId").GetGuid(), card.GetProperty("ledgerAccountId").GetGuid());
        Assert.Equal("USD", card.GetProperty("currency").GetString());
        Assert.Equal("active", card.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Blocking_a_card_changes_the_status_seen_by_get_card()
    {
        HttpClient client = factory.CreateClient();
        Guid id = (await CreateCardAsync(client, "BRL")).GetProperty("id").GetGuid();

        HttpResponseMessage blockResponse = await client.PostAsync($"/cards/{id}/block", content: null);

        Assert.Equal(HttpStatusCode.OK, blockResponse.StatusCode);
        Assert.Equal("blocked", (await ReadJsonAsync(blockResponse)).GetProperty("status").GetString());
        JsonElement card = await ReadJsonAsync(await client.GetAsync($"/cards/{id}"));
        Assert.Equal("blocked", card.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Blocking_a_blocked_card_is_accepted_and_keeps_it_blocked()
    {
        HttpClient client = factory.CreateClient();
        Guid id = (await CreateCardAsync(client, "BRL")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/cards/{id}/block", content: null)).StatusCode);

        HttpResponseMessage response = await client.PostAsync($"/cards/{id}/block", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("blocked", (await ReadJsonAsync(response)).GetProperty("status").GetString());
        JsonElement card = await ReadJsonAsync(await client.GetAsync($"/cards/{id}"));
        Assert.Equal("blocked", card.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_unknown_card_returns_not_found_with_code()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/cards/{Guid.NewGuid()}");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "card_not_found");
    }

    [Fact]
    public async Task Block_unknown_card_returns_not_found_with_code()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync($"/cards/{Guid.NewGuid()}/block", content: null);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "card_not_found");
    }

    [Theory]
    [InlineData("brl")]
    [InlineData("REAL")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Post_cards_with_invalid_currency_returns_bad_request_with_code(string? currency)
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/cards", new { currency });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_currency");
    }

    [Theory]
    [InlineData("{ \"currency\": ")]
    [InlineData("")]
    public async Task Post_cards_with_malformed_body_returns_bad_request_problem(string json)
    {
        HttpClient client = factory.CreateClient();
        using StringContent content = new(json, Encoding.UTF8, "application/json");

        HttpResponseMessage response = await client.PostAsync("/cards", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_cards_with_ledger_down_returns_service_unavailable_and_saves_no_card()
    {
        int cardsBefore = await CountCardsAsync();
        using WebApplicationFactory<Program> ledgerDown = factory.WithLedgerDown();
        HttpClient client = ledgerDown.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/cards", new { currency = "BRL" });

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "ledger_unavailable");
        Assert.Equal(cardsBefore, await CountCardsAsync());
    }

    private async Task<int> CountCardsAsync()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        return await dbContext.Cards.CountAsync();
    }

    private static async Task<JsonElement> CreateCardAsync(HttpClient client, string currency)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/cards", new { currency });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
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
