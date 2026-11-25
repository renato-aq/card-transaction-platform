using CardPlatform.Fraud.Application.Purchases;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Application.Tests.Purchases;

public class RecordVoidedPurchaseHandlerTests
{
    private static readonly DateTimeOffset AuthorizedAt = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset CompletedAt = AuthorizedAt.AddHours(2);

    private readonly FakeCardPurchaseRepository _purchases = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly RecordVoidedPurchaseHandler _handler;
    private readonly RecordVoidedPurchase _command = new(
        Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), 30m, "BRL", AuthorizedAt, CompletedAt);

    public RecordVoidedPurchaseHandlerTests()
    {
        _unitOfWork = new FakeUnitOfWork(_purchases);
        _handler = new RecordVoidedPurchaseHandler(_purchases, _unitOfWork);
    }

    [Fact]
    public async Task Marks_the_recorded_approved_purchase_voided()
    {
        CardPurchase purchase = SeedApproved();

        Result<RecordingOutcome> result = await _handler.HandleAsync(_command);

        Assert.Equal(RecordingOutcome.Updated, result.Value);
        Assert.Equal(PurchaseStatus.Voided, purchase.Status);
        Assert.Equal(CompletedAt, purchase.CompletedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task The_same_event_again_changes_nothing()
    {
        CardPurchase purchase = SeedApproved();
        await _handler.HandleAsync(_command);

        Result<RecordingOutcome> again = await _handler.HandleAsync(_command with { CompletedAt = CompletedAt.AddMinutes(1) });

        Assert.Equal(RecordingOutcome.AlreadyRecorded, again.Value);
        Assert.Equal(CompletedAt, purchase.CompletedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_purchase_whose_approval_is_not_in_the_history_is_recorded_as_approved_and_voided()
    {
        Result<RecordingOutcome> result = await _handler.HandleAsync(_command);

        Assert.Equal(RecordingOutcome.Recorded, result.Value);
        Assert.Equal(1, _unitOfWork.SaveCount);
        CardPurchase purchase = Assert.Single((await _purchases.GetHistoryAsync(_command.CardId)).Approved);
        Assert.Equal(_command.AuthorizationId, purchase.Id);
        Assert.Equal(_command.MerchantAccountId, purchase.MerchantAccountId);
        Assert.Equal(Money.Create(30m, "BRL").Value, purchase.Amount);
        Assert.Equal(AuthorizedAt, purchase.AuthorizedAt);
        Assert.Equal(PurchaseStatus.Voided, purchase.Status);
        Assert.Equal(CompletedAt, purchase.CompletedAt);
    }

    [Fact]
    public async Task A_purchase_already_captured_is_a_conflict_and_is_left_unchanged()
    {
        CardPurchase purchase = SeedApproved();
        purchase.Capture(AuthorizedAt.AddHours(1));

        Result<RecordingOutcome> result = await _handler.HandleAsync(_command);

        Assert.Equal(CardPurchase.NotCompletableCode, result.Error.Code);
        Assert.Equal(PurchaseStatus.Captured, purchase.Status);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_declined_purchase_is_a_conflict_and_is_left_unchanged()
    {
        CardPurchase purchase = CardPurchase.Decline(
            _command.AuthorizationId, _command.CardId, _command.MerchantAccountId, Money.Create(30m, "BRL").Value, "card_blocked", AuthorizedAt).Value;
        _purchases.Seed(purchase);

        Result<RecordingOutcome> result = await _handler.HandleAsync(_command);

        Assert.Equal(CardPurchase.NotCompletableCode, result.Error.Code);
        Assert.Equal(PurchaseStatus.Declined, purchase.Status);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_invalid_amount_of_an_unknown_purchase_is_rejected_and_nothing_is_saved()
    {
        Result<RecordingOutcome> result = await _handler.HandleAsync(_command with { Currency = "R$" });

        Assert.Equal(Money.InvalidCurrencyCode, result.Error.Code);
        Assert.Empty(_purchases.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private CardPurchase SeedApproved()
    {
        CardPurchase purchase = CardPurchase.Approve(
            _command.AuthorizationId, _command.CardId, _command.MerchantAccountId, Money.Create(30m, "BRL").Value, AuthorizedAt).Value;
        _purchases.Seed(purchase);
        return purchase;
    }
}
