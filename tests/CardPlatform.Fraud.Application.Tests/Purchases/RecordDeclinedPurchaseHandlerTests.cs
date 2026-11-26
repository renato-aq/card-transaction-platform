using CardPlatform.Fraud.Application.Purchases;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Application.Tests.Purchases;

public class RecordDeclinedPurchaseHandlerTests
{
    private static readonly DateTimeOffset AuthorizedAt = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeCardPurchaseRepository _purchases = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly RecordDeclinedPurchaseHandler _handler;
    private readonly RecordDeclinedPurchase _command = new(
        Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), 500m, "BRL", AuthorizedAt, "insufficient_funds");

    public RecordDeclinedPurchaseHandlerTests()
    {
        _unitOfWork = new FakeUnitOfWork(_purchases);
        _handler = new RecordDeclinedPurchaseHandler(_purchases, _unitOfWork);
    }

    [Fact]
    public async Task Adds_the_declined_purchase_with_its_reason_to_the_card_history()
    {
        Result<RecordingOutcome> result = await _handler.HandleAsync(_command);

        Assert.Equal(RecordingOutcome.Recorded, result.Value);
        Assert.Equal(1, _unitOfWork.SaveCount);
        CardHistory history = await _purchases.GetHistoryAsync(_command.CardId);
        Assert.Empty(history.Approved);
        CardPurchase purchase = Assert.Single(history.Declined);
        Assert.Equal(_command.AuthorizationId, purchase.Id);
        Assert.Equal("insufficient_funds", purchase.DeclineReason);
        Assert.Equal(Money.Create(500m, "BRL").Value, purchase.Amount);
        Assert.Equal(AuthorizedAt, purchase.AuthorizedAt);
    }

    [Fact]
    public async Task The_same_decline_again_changes_nothing()
    {
        await _handler.HandleAsync(_command);

        Result<RecordingOutcome> again = await _handler.HandleAsync(_command);

        Assert.Equal(RecordingOutcome.AlreadyRecorded, again.Value);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Single(_purchases.Saved);
    }

    [Fact]
    public async Task A_decline_without_reason_is_rejected_and_nothing_is_saved()
    {
        Result<RecordingOutcome> result = await _handler.HandleAsync(_command with { Reason = "" });

        Assert.Equal(CardPurchase.InvalidPurchaseCode, result.Error.Code);
        Assert.Empty(_purchases.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_invalid_amount_is_rejected_and_nothing_is_saved()
    {
        Result<RecordingOutcome> result = await _handler.HandleAsync(_command with { Amount = 1.234m });

        Assert.Equal(Money.InvalidAmountCode, result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}
