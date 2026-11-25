using CardPlatform.Fraud.Application.Purchases;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Application.Tests.Purchases;

public class RecordApprovedPurchaseHandlerTests
{
    private static readonly DateTimeOffset AuthorizedAt = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeCardPurchaseRepository _purchases = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly RecordApprovedPurchaseHandler _handler;
    private readonly RecordApprovedPurchase _command = new(
        Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), 42.5m, "BRL", AuthorizedAt);

    public RecordApprovedPurchaseHandlerTests()
    {
        _unitOfWork = new FakeUnitOfWork(_purchases);
        _handler = new RecordApprovedPurchaseHandler(_purchases, _unitOfWork);
    }

    [Fact]
    public async Task Adds_the_approved_purchase_to_the_card_history()
    {
        Result<RecordingOutcome> result = await _handler.HandleAsync(_command);

        Assert.Equal(RecordingOutcome.Recorded, result.Value);
        Assert.Equal(1, _unitOfWork.SaveCount);
        CardPurchase purchase = Assert.Single((await _purchases.GetHistoryAsync(_command.CardId)).Approved);
        Assert.Equal(_command.AuthorizationId, purchase.Id);
        Assert.Equal(_command.MerchantAccountId, purchase.MerchantAccountId);
        Assert.Equal(Money.Create(42.5m, "BRL").Value, purchase.Amount);
        Assert.Equal(AuthorizedAt, purchase.AuthorizedAt);
        Assert.Equal(PurchaseStatus.Approved, purchase.Status);
    }

    [Fact]
    public async Task The_same_approval_again_changes_nothing()
    {
        await _handler.HandleAsync(_command);

        Result<RecordingOutcome> again = await _handler.HandleAsync(_command);

        Assert.Equal(RecordingOutcome.AlreadyRecorded, again.Value);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Single(_purchases.Saved);
    }

    [Fact]
    public async Task An_approval_arriving_after_its_capture_leaves_the_captured_purchase_as_it_is()
    {
        CardPurchase captured = CardPurchase.Approve(
            _command.AuthorizationId, _command.CardId, _command.MerchantAccountId, Money.Create(42.5m, "BRL").Value, AuthorizedAt).Value;
        captured.Capture(AuthorizedAt.AddHours(1));
        _purchases.Seed(captured);

        Result<RecordingOutcome> result = await _handler.HandleAsync(_command);

        Assert.Equal(RecordingOutcome.AlreadyRecorded, result.Value);
        Assert.Equal(PurchaseStatus.Captured, captured.Status);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_invalid_amount_is_rejected_and_nothing_is_saved()
    {
        Result<RecordingOutcome> result = await _handler.HandleAsync(_command with { Currency = "brl" });

        Assert.Equal(Money.InvalidCurrencyCode, result.Error.Code);
        Assert.Empty(_purchases.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}
