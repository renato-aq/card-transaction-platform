using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Fraud.Domain.Rules;

using static CardPlatform.Fraud.Domain.Tests.Purchases.PurchaseSamples;

namespace CardPlatform.Fraud.Domain.Tests.Rules;

public class VelocityRuleTests
{
    private static readonly VelocityRule Rule = new(threshold: 5, window: TimeSpan.FromSeconds(60));

    private readonly Guid _cardId = Guid.NewGuid();

    [Fact]
    public void The_nth_approved_purchase_within_the_window_fires_the_rule()
    {
        CardPurchase purchase = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardHistory history = new(_cardId, PreviousApproved(count: 4, spacing: TimeSpan.FromSeconds(10)));

        RuleViolation? violation = Rule.Evaluate(purchase, CardActivity.Of(purchase, history, Rule.Window));

        Assert.NotNull(violation);
        Assert.Equal(VelocityRule.Code, violation.Rule);
        Assert.Equal("5 approved purchases of the card within 60 s (threshold: 5).", violation.Reason);
    }

    [Fact]
    public void The_n_minus_1th_approved_purchase_within_the_window_does_not_fire_the_rule()
    {
        CardPurchase purchase = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardHistory history = new(_cardId, PreviousApproved(count: 3, spacing: TimeSpan.FromSeconds(10)));

        Assert.Null(Rule.Evaluate(purchase, CardActivity.Of(purchase, history, Rule.Window)));
    }

    [Fact]
    public void More_than_n_approved_purchases_within_the_window_fire_the_rule()
    {
        CardPurchase purchase = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardHistory history = new(_cardId, PreviousApproved(count: 6, spacing: TimeSpan.FromSeconds(5)));

        RuleViolation? violation = Rule.Evaluate(purchase, CardActivity.Of(purchase, history, Rule.Window));

        Assert.StartsWith("7 approved purchases", violation?.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_purchase_just_inside_the_window_counts()
    {
        CardPurchase purchase = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardHistory history = new(_cardId,
        [
            .. PreviousApproved(count: 3, spacing: TimeSpan.FromSeconds(1)),
            Approved(_cardId, authorizedAt: AuthorizedAt - Rule.Window + TimeSpan.FromMicroseconds(1)),
        ]);

        Assert.NotNull(Rule.Evaluate(purchase, CardActivity.Of(purchase, history, Rule.Window)));
    }

    [Fact]
    public void A_purchase_exactly_one_window_earlier_is_outside_the_window()
    {
        CardPurchase purchase = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardHistory history = new(_cardId,
        [
            .. PreviousApproved(count: 3, spacing: TimeSpan.FromSeconds(1)),
            Approved(_cardId, authorizedAt: AuthorizedAt - Rule.Window),
        ]);

        Assert.Null(Rule.Evaluate(purchase, CardActivity.Of(purchase, history, Rule.Window)));
    }

    [Fact]
    public void Purchases_authorized_after_the_judged_one_do_not_count()
    {
        // The window ends at the purchase's own authorization time, not when it is processed.
        CardPurchase purchase = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardHistory history = new(_cardId,
        [
            .. PreviousApproved(count: 3, spacing: TimeSpan.FromSeconds(1)),
            Approved(_cardId, authorizedAt: AuthorizedAt.AddSeconds(1)),
        ]);

        Assert.Null(Rule.Evaluate(purchase, CardActivity.Of(purchase, history, Rule.Window)));
    }

    [Fact]
    public void Declined_purchases_do_not_count()
    {
        CardPurchase purchase = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardHistory history = new(_cardId,
        [
            .. PreviousApproved(count: 3, spacing: TimeSpan.FromSeconds(1)),
            Declined(_cardId, authorizedAt: AuthorizedAt.AddSeconds(-5)),
            Declined(_cardId, authorizedAt: AuthorizedAt.AddSeconds(-6)),
        ]);

        Assert.Null(Rule.Evaluate(purchase, CardActivity.Of(purchase, history, Rule.Window)));
    }

    [Fact]
    public void Captured_and_voided_purchases_still_count_as_approved()
    {
        CardPurchase purchase = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardPurchase[] previous = PreviousApproved(count: 4, spacing: TimeSpan.FromSeconds(1));
        previous[0].Capture(AuthorizedAt);
        previous[1].Void(AuthorizedAt);

        Assert.NotNull(Rule.Evaluate(purchase, CardActivity.Of(purchase, new CardHistory(_cardId, previous), Rule.Window)));
    }

    [Fact]
    public void Purchases_in_any_currency_count()
    {
        CardPurchase purchase = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardHistory history = new(_cardId,
        [
            .. PreviousApproved(count: 2, spacing: TimeSpan.FromSeconds(1)),
            Approved(_cardId, authorizedAt: AuthorizedAt.AddSeconds(-5), currency: "USD"),
            Approved(_cardId, authorizedAt: AuthorizedAt.AddSeconds(-6), currency: "EUR"),
        ]);

        Assert.NotNull(Rule.Evaluate(purchase, CardActivity.Of(purchase, history, Rule.Window)));
    }

    [Fact]
    public void The_judged_purchase_counts_once_even_when_the_history_already_contains_it()
    {
        CardPurchase purchase = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardHistory history = new(_cardId, [.. PreviousApproved(count: 3, spacing: TimeSpan.FromSeconds(1)), purchase]);

        Assert.Null(Rule.Evaluate(purchase, CardActivity.Of(purchase, history, Rule.Window)));
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(0, 60)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    public void Invalid_limits_are_refused(int threshold, int windowSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VelocityRule(threshold, TimeSpan.FromSeconds(windowSeconds)));
    }

    private CardPurchase[] PreviousApproved(int count, TimeSpan spacing)
    {
        return [.. Enumerable.Range(1, count).Select(index => Approved(_cardId, authorizedAt: AuthorizedAt - (spacing * index)))];
    }
}
