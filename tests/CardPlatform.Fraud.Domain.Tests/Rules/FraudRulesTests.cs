using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Fraud.Domain.Rules;

using static CardPlatform.Fraud.Domain.Tests.Purchases.PurchaseSamples;

namespace CardPlatform.Fraud.Domain.Tests.Rules;

public class FraudRulesTests
{
    private readonly Guid _cardId = Guid.NewGuid();

    [Fact]
    public void The_defaults_are_5_purchases_in_60_seconds_and_3_times_the_average_of_at_least_5()
    {
        Assert.Equal(5, FraudRules.Default.Velocity.Threshold);
        Assert.Equal(TimeSpan.FromSeconds(60), FraudRules.Default.Velocity.Window);
        Assert.Equal(3m, FraudRules.Default.AmountAboveAverage.Multiplier);
        Assert.Equal(5, FraudRules.Default.AmountAboveAverage.MinimumHistory);
    }

    [Fact]
    public void A_normal_purchase_raises_no_suspicion()
    {
        CardPurchase purchase = Approved(_cardId, 120m, AuthorizedAt);
        CardHistory history = new(_cardId, Spread(count: 10, amount: 100m, spacing: TimeSpan.FromHours(1)));

        Assert.Null(FraudRules.Default.Evaluate(purchase, history));
    }

    [Fact]
    public void One_rule_firing_raises_a_suspicion_naming_only_that_rule()
    {
        CardPurchase purchase = Approved(_cardId, 1_000m, AuthorizedAt);
        CardHistory history = new(_cardId, Spread(count: 5, amount: 100m, spacing: TimeSpan.FromHours(1)));

        FraudSuspicion? suspicion = FraudRules.Default.Evaluate(purchase, history);

        Assert.NotNull(suspicion);
        Assert.Equal(_cardId, suspicion.CardId);
        Assert.Equal(purchase.Id, suspicion.AuthorizationId);
        Assert.Equal(AmountAboveAverageRule.Code, suspicion.Rule);
        Assert.Equal(Assert.Single(suspicion.Violations).Reason, suspicion.Reason);
    }

    [Fact]
    public void Both_rules_firing_raise_a_single_suspicion_with_both_rules_and_reasons()
    {
        CardPurchase purchase = Approved(_cardId, 1_000m, AuthorizedAt);
        CardHistory history = new(_cardId, Spread(count: 5, amount: 100m, spacing: TimeSpan.FromSeconds(5)));

        FraudSuspicion? suspicion = FraudRules.Default.Evaluate(purchase, history);

        Assert.NotNull(suspicion);
        Assert.Equal("velocity,amount_above_average", suspicion.Rule);
        Assert.Equal([VelocityRule.Code, AmountAboveAverageRule.Code], suspicion.Violations.Select(static v => v.Rule));
        Assert.Equal(
            "6 approved purchases of the card within 60 s (threshold: 5). "
            + "Amount 1000.00 BRL is above 3x the average of 100.00 BRL over 5 previous approved purchases in BRL.",
            suspicion.Reason);
    }

    [Fact]
    public void A_declined_purchase_is_not_judged()
    {
        CardPurchase purchase = Declined(_cardId, 1_000m, AuthorizedAt);
        CardHistory history = new(_cardId, Spread(count: 5, amount: 100m, spacing: TimeSpan.FromSeconds(5)));

        Assert.Null(FraudRules.Default.Evaluate(purchase, history));
    }

    [Fact]
    public void The_activity_counts_only_previous_approved_purchases_of_the_card()
    {
        CardPurchase purchase = Approved(_cardId, 50m, AuthorizedAt);
        CardHistory history = new(_cardId,
        [
            Approved(_cardId, 10m, AuthorizedAt.AddSeconds(-10)),
            Approved(_cardId, 20m, AuthorizedAt.AddMinutes(-10)),
            Approved(_cardId, 30m, AuthorizedAt.AddSeconds(-20), currency: "USD"),
            Declined(_cardId, 40m, AuthorizedAt.AddSeconds(-30)),
            Approved(_cardId, 60m, AuthorizedAt.AddSeconds(10)),
            purchase,
        ]);

        CardActivity activity = CardActivity.Of(purchase, history, TimeSpan.FromSeconds(60));

        Assert.Equal(2, activity.RecentApprovedCount);
        Assert.Equal(new SpendingSummary(2, 30m), activity.PreviousSpending);
        Assert.Equal(15m, activity.PreviousSpending.Average);
    }

    [Fact]
    public void The_activity_of_a_purchase_needs_the_history_of_its_own_card()
    {
        CardPurchase purchase = Approved(_cardId);

        Assert.Throws<ArgumentException>(() => CardActivity.Of(purchase, new CardHistory(Guid.NewGuid(), []), TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void A_suspicion_needs_at_least_one_violation()
    {
        Assert.Throws<ArgumentException>(() => new FraudSuspicion(_cardId, Guid.NewGuid(), []));
    }

    private CardPurchase[] Spread(int count, decimal amount, TimeSpan spacing)
    {
        return [.. Enumerable.Range(1, count).Select(index => Approved(_cardId, amount, AuthorizedAt - (spacing * index)))];
    }
}
