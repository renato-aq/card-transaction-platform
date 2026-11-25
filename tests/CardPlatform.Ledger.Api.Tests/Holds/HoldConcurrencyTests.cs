using System.Net;

using CardPlatform.Ledger.Api.Tests.Infrastructure;

using Xunit.Abstractions;

namespace CardPlatform.Ledger.Api.Tests.Holds;

/// <summary>
/// Simultaneous hold requests (ADR 0006 and 0007). Holds on the same card compete for the card and the holds account
/// rows; a request that keeps conflicting after its retries is refused with <c>409 concurrency_conflict</c> and saves
/// nothing. What matters is that the balances always match the successful holds, the card never goes negative, the
/// holds account of a currency is created once, and a repeated hold id is placed once. Each test uses a currency of its
/// own, so the holds account and its balance belong to that test alone.
/// </summary>
[Collection(LedgerApiCollectionFixture.Name)]
public class HoldConcurrencyTests(LedgerApiFactory factory, ITestOutputHelper output)
{
    private const int Requests = 20;
    private const decimal Amount = 10m;

    private readonly HoldApi _api = new(factory, factory.CreateClient());

    [Fact]
    public async Task Simultaneous_holds_on_the_same_card_never_overdraw_it()
    {
        const string currency = "HCC";
        const int affordable = 5;
        const decimal topUp = affordable * Amount;
        (Guid settlement, Guid card) = await _api.OpenFundedCardAsync(topUp, currency);

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, Requests)
            .Select(_ => _api.PlaceAsync(Guid.CreateVersion7(), card, Amount, currency)));
        Outcomes outcomes = await CountAsync(responses);

        Assert.Equal(0, outcomes.Repeated);
        Assert.True(outcomes.Created <= affordable, $"{outcomes.Created} holds were placed, only {affordable} were affordable.");

        // Exactly k succeed unless requests were abandoned on conflicts: a hold refused for insufficient funds read a
        // card balance below the amount, and that balance only decreases, so after it no other hold can succeed.
        if (outcomes.InsufficientFunds > 0)
        {
            Assert.Equal(affordable, outcomes.Created);
        }

        Assert.True(
            affordable - outcomes.Created <= outcomes.Conflicts,
            "Every affordable hold that was not placed must have been refused by a concurrency conflict.");

        Guid holds = await _api.GetHoldsAccountAsync(currency);
        decimal settlementBalance = await _api.GetBalanceAsync(settlement);
        decimal cardBalance = await _api.GetBalanceAsync(card);
        decimal holdsBalance = await _api.GetBalanceAsync(holds);
        Assert.True(cardBalance >= 0m, $"The card balance is {cardBalance}.");
        Assert.Equal(topUp - (outcomes.Created * Amount), cardBalance);
        Assert.Equal(outcomes.Created * Amount, holdsBalance);
        Assert.Equal(0m, settlementBalance + cardBalance + holdsBalance);
        Assert.Equal(outcomes.Created + 1, await _api.CountEntriesAsync(card));
        Assert.Equal(outcomes.Created, await _api.CountEntriesAsync(holds));
        Assert.Equal(outcomes.Created, await _api.CountHoldsAsync(card));
    }

    [Fact]
    public async Task Simultaneous_first_holds_in_a_currency_create_a_single_holds_account()
    {
        const string currency = "HFU";
        List<Guid> cards = [];
        for (int i = 0; i < Requests; i++)
        {
            cards.Add((await _api.OpenFundedCardAsync(Amount, currency)).Card);
        }

        Assert.Empty(await _api.FindHoldsAccountsAsync(currency));

        HttpResponseMessage[] responses = await Task.WhenAll(cards
            .Select(card => _api.PlaceAsync(Guid.CreateVersion7(), card, Amount, currency)));
        Outcomes outcomes = await CountAsync(responses);

        Assert.Equal(0, outcomes.InsufficientFunds);
        Assert.Equal(0, outcomes.Repeated);
        Assert.True(outcomes.Created > 0, "At least one hold must be placed.");
        Guid holds = await _api.GetHoldsAccountAsync(currency);
        Assert.Equal(outcomes.Created * Amount, await _api.GetBalanceAsync(holds));
        Assert.Equal(outcomes.Created, await _api.CountEntriesAsync(holds));
    }

    [Fact]
    public async Task Simultaneous_repetitions_of_the_same_hold_place_it_once()
    {
        const string currency = "HRP";
        (_, Guid card) = await _api.OpenFundedCardAsync(100m, currency);
        Guid id = Guid.CreateVersion7();

        HttpResponseMessage[] responses = await Task.WhenAll(Enumerable.Range(0, Requests)
            .Select(_ => _api.PlaceAsync(id, card, Amount, currency)));
        Outcomes outcomes = await CountAsync(responses);

        // A conflict means another request saved first, and only placing this hold saves anything here, so the hold
        // was placed exactly once; the other requests either saw it (200) or gave up (409).
        Assert.Equal(1, outcomes.Created);
        Assert.Equal(0, outcomes.InsufficientFunds);
        Guid holds = await _api.GetHoldsAccountAsync(currency);
        Assert.Equal(100m - Amount, await _api.GetBalanceAsync(card));
        Assert.Equal(Amount, await _api.GetBalanceAsync(holds));
        Assert.Equal(2, await _api.CountEntriesAsync(card));
        Assert.Equal(1, await _api.CountHoldsAsync(card));
        HoldApi.AssertHold(await _api.GetHoldAsync(id), id, card, Amount, currency, "pending");
    }

    private async Task<Outcomes> CountAsync(HttpResponseMessage[] responses)
    {
        Outcomes outcomes = new();
        foreach (HttpResponseMessage response in responses)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.Created:
                    outcomes.Created++;
                    break;
                case HttpStatusCode.OK:
                    outcomes.Repeated++;
                    break;
                case HttpStatusCode.UnprocessableEntity:
                    Assert.Equal("insufficient_funds", await HoldApi.ReadCodeAsync(response));
                    outcomes.InsufficientFunds++;
                    break;
                case HttpStatusCode.Conflict:
                    Assert.Equal("concurrency_conflict", await HoldApi.ReadCodeAsync(response));
                    outcomes.Conflicts++;
                    break;
                default:
                    Assert.Fail($"Unexpected status {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                    break;
            }
        }

        output.WriteLine(
            $"{responses.Length} holds: {outcomes.Created} created, {outcomes.Repeated} repeated, " +
            $"{outcomes.InsufficientFunds} insufficient_funds, {outcomes.Conflicts} concurrency_conflict.");
        return outcomes;
    }

    private sealed class Outcomes
    {
        public int Created { get; set; }

        public int Repeated { get; set; }

        public int InsufficientFunds { get; set; }

        public int Conflicts { get; set; }
    }
}
