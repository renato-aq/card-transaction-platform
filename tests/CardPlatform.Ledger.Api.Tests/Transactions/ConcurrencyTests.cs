using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using CardPlatform.Ledger.Api.Tests.Infrastructure;
using CardPlatform.Ledger.Domain.Transactions;
using CardPlatform.Ledger.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit.Abstractions;

namespace CardPlatform.Ledger.Api.Tests.Transactions;

/// <summary>
/// Simultaneous purchases on the same card (ADR 0006). Each purchase debits the card and credits the same merchant,
/// so every request competes for the same two rows. A request that keeps conflicting after its retries is refused
/// with <c>409 concurrency_conflict</c> and saves nothing; what matters is that the balances always match the
/// successful purchases and the card never goes negative.
/// </summary>
[Collection(LedgerApiCollectionFixture.Name)]
public class ConcurrencyTests(LedgerApiFactory factory, ITestOutputHelper output)
{
    private const int Purchases = 20;
    private const decimal Amount = 10m;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Simultaneous_purchases_with_enough_balance_for_all_leave_exact_balances()
    {
        const decimal topUp = Purchases * Amount;
        (Guid settlement, Guid card, Guid merchant) = await OpenFundedCardAsync(topUp);

        Outcomes outcomes = await PurchaseSimultaneouslyAsync(card, merchant);

        // With money for every purchase, the only acceptable refusal is a conflict that outlasted the retries.
        Assert.Equal(0, outcomes.InsufficientFunds);
        Assert.Equal(Purchases, outcomes.Succeeded + outcomes.Conflicts);
        Assert.True(outcomes.Succeeded > 0, "At least one purchase must succeed.");
        await AssertBalancesAsync(settlement, card, merchant, topUp, outcomes.Succeeded);
    }

    [Fact]
    public async Task Simultaneous_purchases_with_balance_for_only_some_never_overdraw_the_card()
    {
        const int affordable = 5;
        const decimal topUp = affordable * Amount;
        (Guid settlement, Guid card, Guid merchant) = await OpenFundedCardAsync(topUp);

        Outcomes outcomes = await PurchaseSimultaneouslyAsync(card, merchant);

        Assert.Equal(Purchases, outcomes.Succeeded + outcomes.InsufficientFunds + outcomes.Conflicts);
        Assert.True(outcomes.Succeeded <= affordable, $"{outcomes.Succeeded} purchases succeeded, only {affordable} were affordable.");

        // Exactly k succeed unless requests were abandoned on conflicts: a purchase refused for insufficient funds
        // read a balance below the amount, and the balance only decreases, so after it no other purchase can succeed.
        if (outcomes.InsufficientFunds > 0)
        {
            Assert.Equal(affordable, outcomes.Succeeded);
        }

        Assert.True(
            affordable - outcomes.Succeeded <= outcomes.Conflicts,
            "Every affordable purchase that did not succeed must have been refused by a concurrency conflict.");
        await AssertBalancesAsync(settlement, card, merchant, topUp, outcomes.Succeeded);
    }

    private async Task<(Guid Settlement, Guid Card, Guid Merchant)> OpenFundedCardAsync(decimal topUp)
    {
        Guid settlement = await OpenAccountAsync("settlement");
        Guid card = await OpenAccountAsync("card");
        Guid merchant = await OpenAccountAsync("merchant");

        HttpResponseMessage response = await _client.PostAsJsonAsync(
            "/transactions",
            Purchase(settlement, card, topUp));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (settlement, card, merchant);
    }

    private async Task<Outcomes> PurchaseSimultaneouslyAsync(Guid card, Guid merchant)
    {
        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, Purchases)
            .Select(_ => _client.PostAsJsonAsync("/transactions", Purchase(card, merchant, Amount))));

        Outcomes outcomes = new();
        foreach (HttpResponseMessage response in responses)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Created:
                    outcomes.Succeeded++;
                    break;
                case HttpStatusCode.UnprocessableEntity:
                    Assert.Equal("insufficient_funds", await ReadCodeAsync(response));
                    outcomes.InsufficientFunds++;
                    break;
                case HttpStatusCode.Conflict:
                    Assert.Equal("concurrency_conflict", await ReadCodeAsync(response));
                    outcomes.Conflicts++;
                    break;
                default:
                    Assert.Fail($"Unexpected status {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    break;
            }
        }

        output.WriteLine(
            $"{Purchases} purchases: {outcomes.Succeeded} created, {outcomes.InsufficientFunds} insufficient_funds, " +
            $"{outcomes.Conflicts} concurrency_conflict.");
        return outcomes;
    }

    private async Task AssertBalancesAsync(Guid settlement, Guid card, Guid merchant, decimal topUp, int succeeded)
    {
        decimal settlementBalance = await GetBalanceAsync(settlement);
        decimal cardBalance = await GetBalanceAsync(card);
        decimal merchantBalance = await GetBalanceAsync(merchant);

        Assert.True(cardBalance >= 0m, $"The card balance is {cardBalance}.");
        Assert.Equal(topUp - (succeeded * Amount), cardBalance);
        Assert.Equal(succeeded * Amount, merchantBalance);
        Assert.Equal(-topUp, settlementBalance);
        Assert.Equal(0m, settlementBalance + cardBalance + merchantBalance);

        // Only successful purchases saved entries: one per purchase on each side, plus the top-up on the card.
        Assert.Equal(succeeded + 1, await CountEntriesAsync(card));
        Assert.Equal(succeeded, await CountEntriesAsync(merchant));
    }

    private static object Purchase(Guid debited, Guid credited, decimal amount)
    {
        return new
        {
            currency = "BRL",
            entries = new[]
            {
                new { accountId = debited, direction = "debit", amount },
                new { accountId = credited, direction = "credit", amount },
            },
        };
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    }

    private async Task<Guid> OpenAccountAsync(string type)
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/accounts", new { type, currency = "BRL" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<decimal> GetBalanceAsync(Guid accountId)
    {
        HttpResponseMessage response = await _client.GetAsync($"/accounts/{accountId}/balance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("balance").GetProperty("amount").GetDecimal();
    }

    // Reads the database directly: there is no endpoint to list transactions yet.
    private async Task<int> CountEntriesAsync(Guid accountId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        LedgerDbContext dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        return await dbContext.Set<Entry>().CountAsync(entry => entry.AccountId == accountId);
    }

    private sealed class Outcomes
    {
        public int Succeeded { get; set; }

        public int InsufficientFunds { get; set; }

        public int Conflicts { get; set; }
    }
}
