using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using CardPlatform.EndToEnd.Tests.Infrastructure;

namespace CardPlatform.EndToEnd.Tests;

/// <summary>
/// The fraud loop end to end (spec 006): purchases authorized by the Authorization API are published on
/// <c>authorizations</c>, judged by the Fraud Worker, whose <c>FraudSuspected</c> goes back on <c>fraud-signals</c> and
/// blocks the card in the Authorization, which then declines the card's next purchase with <c>card_blocked</c>. Every
/// hop is real: the outbox publishers, Kafka, the idempotent consumers, the databases and the Ledger.
/// </summary>
[Collection(PlatformCollectionFixture.Name)]
public sealed class FraudBlockingTests(PlatformFixture platform)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private readonly HttpClient _client = platform.Authorization.CreateClient();
    private readonly HttpClient _ledger = platform.Ledger.CreateClient();

    [Fact]
    public async Task Rapid_purchases_of_a_card_block_it_and_its_next_purchase_is_declined_with_card_blocked()
    {
        Guid cardId = await CreateFundedCardAsync(1_000m);
        Guid merchantAccountId = await OpenLedgerAccountAsync("merchant");

        // Enough approved purchases within the velocity window to fire the (lowered) velocity rule.
        for (int purchase = 0; purchase < PlatformFixture.VelocityThreshold; purchase++)
        {
            JsonElement approved = await AuthorizeAsync(cardId, merchantAccountId, 10m);
            Assert.Equal("approved", approved.GetProperty("status").GetString());
        }

        await WaitUntilBlockedAsync(cardId);

        JsonElement declined = await AuthorizeAsync(cardId, merchantAccountId, 10m);
        Assert.Equal("declined", declined.GetProperty("status").GetString());
        Assert.Equal("card_blocked", declined.GetProperty("declineReason").GetString());
    }

    private async Task WaitUntilBlockedAsync(Guid cardId)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (await CardStatusAsync(cardId) != "blocked")
        {
            Assert.True(
                DateTime.UtcNow < deadline,
                string.Create(CultureInfo.InvariantCulture, $"Card {cardId} was not blocked within {Timeout}."));
            await Task.Delay(200);
        }
    }

    private async Task<JsonElement> AuthorizeAsync(Guid cardId, Guid merchantAccountId, decimal amount)
    {
        HttpResponseMessage response = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/authorizations")
        {
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } },
            Content = JsonContent.Create(new { cardId, merchantAccountId, amount, currency = "BRL" }),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string?> CardStatusAsync(Guid cardId)
    {
        JsonElement card = await _client.GetFromJsonAsync<JsonElement>($"/cards/{cardId}");
        return card.GetProperty("status").GetString();
    }

    private async Task<Guid> CreateFundedCardAsync(decimal balance)
    {
        HttpResponseMessage cardResponse = await _client.PostAsJsonAsync("/cards", new { currency = "BRL" });
        Assert.Equal(HttpStatusCode.Created, cardResponse.StatusCode);
        JsonElement card = await cardResponse.Content.ReadFromJsonAsync<JsonElement>();
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

        return card.GetProperty("id").GetGuid();
    }

    private async Task<Guid> OpenLedgerAccountAsync(string type)
    {
        HttpResponseMessage response = await _ledger.PostAsJsonAsync("/accounts", new { type, currency = "BRL" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
