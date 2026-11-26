using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Fraud.Domain.Rules;

using static CardPlatform.Fraud.Domain.Tests.Purchases.PurchaseSamples;

namespace CardPlatform.Fraud.Domain.Tests.Rules;

public class AmountAboveAverageRuleTests
{
    private static readonly AmountAboveAverageRule Rule = new(multiplier: 3m, minimumHistory: 5);

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly Guid _cardId = Guid.NewGuid();

    [Fact]
    public void An_amount_above_x_times_the_average_fires_the_rule()
    {
        CardPurchase purchase = Approved(_cardId, 300.01m, AuthorizedAt);
        CardHistory history = new(_cardId, PreviousApproved(100m, 100m, 100m, 50m, 150m));

        RuleViolation? violation = Evaluate(purchase, history);

        Assert.NotNull(violation);
        Assert.Equal(AmountAboveAverageRule.Code, violation.Rule);
        Assert.Equal(
            "Amount 300.01 BRL is above 3x the average of 100.00 BRL over 5 previous approved purchases in BRL.",
            violation.Reason);
    }

    [Fact]
    public void An_amount_of_exactly_x_times_the_average_does_not_fire_the_rule()
    {
        CardPurchase purchase = Approved(_cardId, 300m, AuthorizedAt);
        CardHistory history = new(_cardId, PreviousApproved(100m, 100m, 100m, 50m, 150m));

        Assert.Null(Evaluate(purchase, history));
    }

    [Fact]
    public void An_average_with_repeating_decimals_is_compared_exactly()
    {
        // Average 100/3 = 33.333...; 3 × average = 100 exactly, so 100.00 does not fire and 100.01 does.
        AmountAboveAverageRule rule = new(multiplier: 3m, minimumHistory: 3);
        CardHistory history = new(_cardId, PreviousApproved(33.33m, 33.33m, 33.34m));

        Assert.Null(rule.Evaluate(Approved(_cardId, 100m, AuthorizedAt), ActivityOf(Approved(_cardId, 100m, AuthorizedAt), history)));
        CardPurchase above = Approved(_cardId, 100.01m, AuthorizedAt);
        Assert.NotNull(rule.Evaluate(above, ActivityOf(above, history)));
    }

    [Fact]
    public void Without_the_minimum_history_the_rule_does_not_fire()
    {
        CardPurchase purchase = Approved(_cardId, 10_000m, AuthorizedAt);
        CardHistory history = new(_cardId, PreviousApproved(10m, 10m, 10m, 10m));

        Assert.Null(Evaluate(purchase, history));
    }

    [Fact]
    public void Without_any_history_the_rule_does_not_fire()
    {
        CardPurchase purchase = Approved(_cardId, 10_000m, AuthorizedAt);

        Assert.Null(Evaluate(purchase, new CardHistory(_cardId, [])));
    }

    [Fact]
    public void With_exactly_the_minimum_history_the_rule_fires()
    {
        CardPurchase purchase = Approved(_cardId, 10_000m, AuthorizedAt);
        CardHistory history = new(_cardId, PreviousApproved(10m, 10m, 10m, 10m, 10m));

        Assert.NotNull(Evaluate(purchase, history));
    }

    [Fact]
    public void Purchases_in_other_currencies_are_neither_averaged_nor_counted_towards_the_minimum()
    {
        CardPurchase purchase = Approved(_cardId, 1_000m, AuthorizedAt);
        CardHistory history = new(_cardId,
        [
            .. PreviousApproved(100m, 100m, 100m, 100m),
            Approved(_cardId, 1m, AuthorizedAt.AddDays(-10), currency: "USD"),
            Approved(_cardId, 1m, AuthorizedAt.AddDays(-11), currency: "USD"),
        ]);

        Assert.Null(Evaluate(purchase, history));
    }

    [Fact]
    public void The_average_is_taken_in_the_currency_of_the_purchase()
    {
        // In BRL the purchase is 10× the average; the large USD purchases do not lift the BRL average.
        CardPurchase purchase = Approved(_cardId, 1_000m, AuthorizedAt);
        CardHistory history = new(_cardId,
        [
            .. PreviousApproved(100m, 100m, 100m, 100m, 100m),
            Approved(_cardId, 50_000m, AuthorizedAt.AddDays(-10), currency: "USD"),
        ]);

        Assert.NotNull(Evaluate(purchase, history));

        CardPurchase inUsd = Approved(_cardId, 1_000m, AuthorizedAt, currency: "USD");
        Assert.Null(Evaluate(inUsd, history));
    }

    [Fact]
    public void Declined_purchases_are_not_averaged()
    {
        CardPurchase purchase = Approved(_cardId, 1_000m, AuthorizedAt);
        CardHistory history = new(_cardId,
        [
            .. PreviousApproved(100m, 100m, 100m, 100m),
            Declined(_cardId, 5_000m, AuthorizedAt.AddDays(-1)),
            Declined(_cardId, 5_000m, AuthorizedAt.AddDays(-2)),
        ]);

        Assert.Null(Evaluate(purchase, history));
    }

    [Fact]
    public void The_judged_purchase_is_not_part_of_its_own_average()
    {
        CardPurchase purchase = Approved(_cardId, 1_000m, AuthorizedAt);
        CardHistory history = new(_cardId, [.. PreviousApproved(100m, 100m, 100m, 100m, 100m), purchase]);

        SpendingSummary previous = ActivityOf(purchase, history).PreviousSpending;

        Assert.Equal(new SpendingSummary(5, 500m), previous);
        Assert.NotNull(Evaluate(purchase, history));
    }

    [Fact]
    public void Purchases_authorized_after_the_judged_one_are_not_previous()
    {
        CardPurchase purchase = Approved(_cardId, 1_000m, AuthorizedAt);
        CardHistory history = new(_cardId,
        [
            .. PreviousApproved(100m, 100m, 100m, 100m),
            Approved(_cardId, 100m, AuthorizedAt.AddMinutes(1)),
        ]);

        Assert.Null(Evaluate(purchase, history));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(0.5, 5)]
    [InlineData(3, 0)]
    public void Invalid_limits_are_refused(decimal multiplier, int minimumHistory)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AmountAboveAverageRule(multiplier, minimumHistory));
    }

    private static RuleViolation? Evaluate(CardPurchase purchase, CardHistory history)
    {
        return Rule.Evaluate(purchase, ActivityOf(purchase, history));
    }

    private static CardActivity ActivityOf(CardPurchase purchase, CardHistory history)
    {
        return CardActivity.Of(purchase, history, Window);
    }

    /// <summary>Approved BRL purchases of the card, one day apart, before <see cref="PurchaseSamples.AuthorizedAt"/>.</summary>
    private CardPurchase[] PreviousApproved(params decimal[] amounts)
    {
        return [.. amounts.Select((amount, index) => Approved(_cardId, amount, AuthorizedAt.AddDays(-(index + 1))))];
    }
}
