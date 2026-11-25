using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Domain.Tests.Authorizations;

public class CardAuthorizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private static readonly Currency Brl = Currency.Create("BRL").Value;

    private static readonly Money TenBrl = Money.Create(10m, Brl).Value;

    private readonly Guid _merchantAccountId = Guid.NewGuid();

    [Fact]
    public void New_id_is_a_uuid_v7()
    {
        Guid id = CardAuthorization.NewId(Now);

        Assert.Equal(7, id.Version);
    }

    [Fact]
    public void Check_card_passes_an_active_card_in_the_amount_currency()
    {
        Assert.Null(CardAuthorization.CheckCard(ActiveCard(), TenBrl));
    }

    [Fact]
    public void Check_card_declines_a_blocked_card()
    {
        Card card = ActiveCard();
        card.Block();

        Assert.Equal(DeclineReason.CardBlocked, CardAuthorization.CheckCard(card, TenBrl));
    }

    [Fact]
    public void Check_card_declines_another_currency()
    {
        Money tenUsd = Money.Create(10m, "USD").Value;

        Assert.Equal(DeclineReason.CurrencyMismatch, CardAuthorization.CheckCard(ActiveCard(), tenUsd));
    }

    [Fact]
    public void Check_card_reports_a_blocked_card_before_a_currency_mismatch()
    {
        Card card = ActiveCard();
        card.Block();
        Money tenUsd = Money.Create(10m, "USD").Value;

        Assert.Equal(DeclineReason.CardBlocked, CardAuthorization.CheckCard(card, tenUsd));
    }

    [Fact]
    public void Approve_returns_an_approved_authorization_without_decline_reason()
    {
        Card card = ActiveCard();
        Guid id = CardAuthorization.NewId(Now);

        CardAuthorization authorization = CardAuthorization.Approve(id, card, _merchantAccountId, TenBrl, Now);

        Assert.Equal(id, authorization.Id);
        Assert.Equal(card.Id, authorization.CardId);
        Assert.Equal(_merchantAccountId, authorization.MerchantAccountId);
        Assert.Equal(TenBrl, authorization.Amount);
        Assert.Equal(AuthorizationStatus.Approved, authorization.Status);
        Assert.Null(authorization.DeclineReason);
        Assert.Equal(Now, authorization.CreatedAt);
    }

    [Fact]
    public void Approve_a_blocked_card_throws()
    {
        Card card = ActiveCard();
        card.Block();

        Assert.Throws<InvalidOperationException>(() =>
            CardAuthorization.Approve(Guid.NewGuid(), card, _merchantAccountId, TenBrl, Now));
    }

    [Theory]
    [InlineData(DeclineReason.CardBlocked)]
    [InlineData(DeclineReason.CurrencyMismatch)]
    [InlineData(DeclineReason.InsufficientFunds)]
    public void Decline_returns_a_declined_authorization_with_its_reason(DeclineReason reason)
    {
        Card card = ActiveCard();
        Guid id = CardAuthorization.NewId(Now);

        CardAuthorization authorization = CardAuthorization.Decline(id, card, _merchantAccountId, TenBrl, reason, Now);

        Assert.Equal(id, authorization.Id);
        Assert.Equal(card.Id, authorization.CardId);
        Assert.Equal(AuthorizationStatus.Declined, authorization.Status);
        Assert.Equal(reason, authorization.DeclineReason);
    }

    [Fact]
    public void Decline_with_unknown_reason_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CardAuthorization.Decline(Guid.NewGuid(), ActiveCard(), _merchantAccountId, TenBrl, (DeclineReason)99, Now));
    }

    [Fact]
    public void Empty_ids_throw()
    {
        Assert.Throws<ArgumentException>(() =>
            CardAuthorization.Approve(Guid.Empty, ActiveCard(), _merchantAccountId, TenBrl, Now));
        Assert.Throws<ArgumentException>(() =>
            CardAuthorization.Approve(Guid.NewGuid(), ActiveCard(), Guid.Empty, TenBrl, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_amount_throws(int amount)
    {
        Money money = Money.Create(amount, Brl).Value;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CardAuthorization.Decline(Guid.NewGuid(), ActiveCard(), _merchantAccountId, money, DeclineReason.CardBlocked, Now));
    }

    private static Card ActiveCard()
    {
        return Card.Create(Guid.NewGuid(), Brl, Now);
    }
}
