using CardPlatform.Fraud.Domain.Purchases;

using static CardPlatform.Fraud.Domain.Tests.Purchases.PurchaseSamples;

namespace CardPlatform.Fraud.Domain.Tests.Purchases;

public class CardHistoryTests
{
    private readonly Guid _cardId = Guid.NewGuid();

    [Fact]
    public void Lists_the_purchases_oldest_first()
    {
        CardPurchase later = Approved(_cardId, authorizedAt: AuthorizedAt.AddMinutes(5));
        CardPurchase earlier = Declined(_cardId, authorizedAt: AuthorizedAt);

        CardHistory history = new(_cardId, [later, earlier]);

        Assert.Equal([earlier, later], history.Purchases);
    }

    [Fact]
    public void Separates_approved_purchases_including_completed_ones_from_declined_ones()
    {
        CardPurchase approved = Approved(_cardId);
        CardPurchase captured = Approved(_cardId);
        captured.Capture(AuthorizedAt.AddHours(1));
        CardPurchase voided = Approved(_cardId);
        voided.Void(AuthorizedAt.AddHours(1));
        CardPurchase declined = Declined(_cardId);

        CardHistory history = new(_cardId, [approved, captured, voided, declined]);

        Assert.Equal(3, history.Approved.Count());
        Assert.DoesNotContain(declined, history.Approved);
        Assert.Equal([declined], history.Declined);
    }

    [Fact]
    public void Approved_purchases_since_an_instant_include_that_instant()
    {
        CardPurchase before = Approved(_cardId, authorizedAt: AuthorizedAt.AddSeconds(-1));
        CardPurchase atStart = Approved(_cardId, authorizedAt: AuthorizedAt);
        CardPurchase after = Approved(_cardId, authorizedAt: AuthorizedAt.AddSeconds(30));
        CardPurchase declined = Declined(_cardId, authorizedAt: AuthorizedAt.AddSeconds(10));

        CardHistory history = new(_cardId, [before, atStart, after, declined]);

        Assert.Equal([atStart, after], history.ApprovedSince(AuthorizedAt));
    }

    [Fact]
    public void Approved_purchases_in_a_currency_leave_other_currencies_out()
    {
        CardPurchase brl = Approved(_cardId, 10m);
        CardPurchase usd = Approved(_cardId, 10m, currency: "USD");

        CardHistory history = new(_cardId, [brl, usd]);

        Assert.Equal([brl], history.ApprovedIn("BRL"));
        Assert.Equal([usd], history.ApprovedIn("USD"));
    }

    [Fact]
    public void A_purchase_of_another_card_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new CardHistory(_cardId, [Approved(Guid.NewGuid())]));
    }

    [Fact]
    public void A_card_without_purchases_has_an_empty_history()
    {
        CardHistory history = new(_cardId, []);

        Assert.Empty(history.Purchases);
        Assert.Empty(history.ApprovedSince(AuthorizedAt));
    }
}
