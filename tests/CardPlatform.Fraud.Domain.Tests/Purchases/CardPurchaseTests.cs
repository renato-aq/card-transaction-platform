using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Fraud.Domain.Purchases;

using static CardPlatform.Fraud.Domain.Tests.Purchases.PurchaseSamples;

namespace CardPlatform.Fraud.Domain.Tests.Purchases;

public class CardPurchaseTests
{
    private readonly Guid _authorizationId = Guid.CreateVersion7();
    private readonly Guid _cardId = Guid.NewGuid();
    private readonly Guid _merchantAccountId = Guid.NewGuid();

    [Fact]
    public void An_approved_purchase_keeps_the_decision_truncated_to_microseconds()
    {
        DateTimeOffset authorizedAt = AuthorizedAt.AddTicks(1234567);

        CardPurchase purchase = CardPurchase.Approve(_authorizationId, _cardId, _merchantAccountId, Brl(30m), authorizedAt).Value;

        Assert.Equal(_authorizationId, purchase.Id);
        Assert.Equal(_cardId, purchase.CardId);
        Assert.Equal(_merchantAccountId, purchase.MerchantAccountId);
        Assert.Equal(Brl(30m), purchase.Amount);
        Assert.Equal(PurchaseStatus.Approved, purchase.Status);
        Assert.True(purchase.WasApproved);
        Assert.Null(purchase.DeclineReason);
        Assert.Null(purchase.CompletedAt);
        Assert.Equal(AuthorizedAt.AddTicks(1234560), purchase.AuthorizedAt);
    }

    [Fact]
    public void A_declined_purchase_is_recorded_with_its_reason_and_does_not_count_as_approved()
    {
        CardPurchase purchase = CardPurchase.Decline(
            _authorizationId, _cardId, _merchantAccountId, Brl(500m), "insufficient_funds", AuthorizedAt).Value;

        Assert.Equal(PurchaseStatus.Declined, purchase.Status);
        Assert.Equal("insufficient_funds", purchase.DeclineReason);
        Assert.False(purchase.WasApproved);
    }

    [Fact]
    public void A_decline_reason_unknown_to_this_version_is_kept_as_it_is()
    {
        CardPurchase purchase = CardPurchase.Decline(
            _authorizationId, _cardId, _merchantAccountId, Brl(5m), "merchant_blocked", AuthorizedAt).Value;

        Assert.Equal("merchant_blocked", purchase.DeclineReason);
    }

    [Fact]
    public void A_decline_needs_a_reason()
    {
        Result<CardPurchase> result = CardPurchase.Decline(_authorizationId, _cardId, _merchantAccountId, Brl(5m), " ", AuthorizedAt);

        Assert.Equal(CardPurchase.InvalidPurchaseCode, result.Error.Code);
    }

    [Fact]
    public void Empty_ids_are_rejected()
    {
        Assert.Equal(
            CardPurchase.InvalidPurchaseCode,
            CardPurchase.Approve(Guid.Empty, _cardId, _merchantAccountId, Brl(5m), AuthorizedAt).Error.Code);
        Assert.Equal(
            CardPurchase.InvalidPurchaseCode,
            CardPurchase.Approve(_authorizationId, Guid.Empty, _merchantAccountId, Brl(5m), AuthorizedAt).Error.Code);
        Assert.Equal(
            CardPurchase.InvalidPurchaseCode,
            CardPurchase.Decline(_authorizationId, _cardId, Guid.Empty, Brl(5m), "card_blocked", AuthorizedAt).Error.Code);
    }

    [Fact]
    public void Capturing_an_approved_purchase_records_when_and_still_counts_it_as_approved()
    {
        CardPurchase purchase = Approved(_cardId);

        Result result = purchase.Capture(AuthorizedAt.AddHours(1).AddTicks(9));

        Assert.True(result.IsSuccess);
        Assert.Equal(PurchaseStatus.Captured, purchase.Status);
        Assert.Equal(AuthorizedAt.AddHours(1), purchase.CompletedAt);
        Assert.True(purchase.WasApproved);
    }

    [Fact]
    public void Voiding_an_approved_purchase_records_when_and_still_counts_it_as_approved()
    {
        CardPurchase purchase = Approved(_cardId);

        Result result = purchase.Void(AuthorizedAt.AddHours(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(PurchaseStatus.Voided, purchase.Status);
        Assert.Equal(AuthorizedAt.AddHours(1), purchase.CompletedAt);
        Assert.True(purchase.WasApproved);
    }

    [Fact]
    public void Completing_a_purchase_the_same_way_again_changes_nothing()
    {
        CardPurchase purchase = Approved(_cardId);
        purchase.Capture(AuthorizedAt.AddHours(1));

        Result again = purchase.Capture(AuthorizedAt.AddHours(2));

        Assert.True(again.IsSuccess);
        Assert.Equal(PurchaseStatus.Captured, purchase.Status);
        Assert.Equal(AuthorizedAt.AddHours(1), purchase.CompletedAt);
    }

    [Fact]
    public void A_purchase_completed_one_way_cannot_be_completed_the_other_way()
    {
        CardPurchase purchase = Approved(_cardId);
        purchase.Void(AuthorizedAt.AddHours(1));

        Result result = purchase.Capture(AuthorizedAt.AddHours(2));

        Assert.Equal(CardPurchase.NotCompletableCode, result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(PurchaseStatus.Voided, purchase.Status);
    }

    [Fact]
    public void A_declined_purchase_cannot_be_completed()
    {
        CardPurchase purchase = Declined(_cardId);

        Assert.Equal(CardPurchase.NotCompletableCode, purchase.Capture(AuthorizedAt).Error.Code);
        Assert.Equal(CardPurchase.NotCompletableCode, purchase.Void(AuthorizedAt).Error.Code);
        Assert.Equal(PurchaseStatus.Declined, purchase.Status);
        Assert.Null(purchase.CompletedAt);
    }
}
