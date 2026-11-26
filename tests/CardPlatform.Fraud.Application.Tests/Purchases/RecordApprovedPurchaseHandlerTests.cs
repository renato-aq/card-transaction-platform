using CardPlatform.Contracts;
using CardPlatform.Contracts.Fraud;
using CardPlatform.Fraud.Application.Purchases;
using CardPlatform.Fraud.Application.Signals;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Fraud.Domain.Rules;

namespace CardPlatform.Fraud.Application.Tests.Purchases;

public class RecordApprovedPurchaseHandlerTests
{
    private static readonly DateTimeOffset AuthorizedAt = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = AuthorizedAt.AddSeconds(2).AddTicks(1234567);

    private readonly FakeCardPurchaseRepository _purchases = new();
    private readonly FakeOutbox _outbox = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly RecordApprovedPurchaseHandler _handler;
    private readonly RecordApprovedPurchase _command = new(
        Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), 42.5m, "BRL", AuthorizedAt);

    public RecordApprovedPurchaseHandlerTests()
    {
        _unitOfWork = new FakeUnitOfWork(_purchases, _outbox);
        _handler = new RecordApprovedPurchaseHandler(
            _purchases, _outbox, _unitOfWork, FraudRules.Default, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Adds_the_approved_purchase_to_the_card_history()
    {
        Result<ApprovedPurchaseRecording> result = await _handler.HandleAsync(_command);

        Assert.Equal(RecordingOutcome.Recorded, result.Value.Outcome);
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

        Result<ApprovedPurchaseRecording> again = await _handler.HandleAsync(_command);

        Assert.Equal(RecordingOutcome.AlreadyRecorded, again.Value.Outcome);
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

        Result<ApprovedPurchaseRecording> result = await _handler.HandleAsync(_command);

        Assert.Equal(RecordingOutcome.AlreadyRecorded, result.Value.Outcome);
        Assert.Equal(PurchaseStatus.Captured, captured.Status);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_invalid_amount_is_rejected_and_nothing_is_saved()
    {
        Result<ApprovedPurchaseRecording> result = await _handler.HandleAsync(_command with { Currency = "brl" });

        Assert.Equal(Money.InvalidCurrencyCode, result.Error.Code);
        Assert.Empty(_purchases.Added);
        Assert.Empty(_outbox.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_purchase_that_fires_no_rule_writes_nothing_to_the_outbox()
    {
        SeedApproved(count: 3, amount: 40m);

        Result<ApprovedPurchaseRecording> result = await _handler.HandleAsync(_command);

        Assert.Null(result.Value.Suspicion);
        Assert.Empty(_outbox.Saved);
        Assert.Empty(_outbox.Added);
    }

    [Fact]
    public async Task A_purchase_that_fires_a_rule_writes_one_fraud_suspected_saved_with_the_purchase()
    {
        // Four approved purchases in the last 60 s: this one is the fifth.
        SeedApproved(count: 4, amount: 40m);

        Result<ApprovedPurchaseRecording> result = await _handler.HandleAsync(_command);

        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Contains(_purchases.Saved, purchase => purchase.Id == _command.AuthorizationId);
        (IIntegrationEvent @event, Guid eventId, DateTimeOffset occurredAt) = Assert.Single(_outbox.Saved);
        FraudSuspected suspected = Assert.IsType<FraudSuspected>(@event);
        Assert.Equal(_command.CardId, suspected.CardId);
        Assert.Equal(_command.AuthorizationId, suspected.AuthorizationId);
        Assert.Equal(VelocityRule.Code, suspected.Rule);
        Assert.Contains("5 approved purchases", suspected.Reason, StringComparison.Ordinal);
        Assert.Equal(Now.TruncateToMicroseconds(), suspected.DetectedAt);
        Assert.Equal(suspected.DetectedAt, occurredAt);
        Assert.Equal(FraudSignals.EventIdOf(_command.AuthorizationId), eventId);
        Assert.Equal(VelocityRule.Code, result.Value.Suspicion?.Rule);
    }

    [Fact]
    public async Task A_purchase_that_fires_both_rules_writes_a_single_fraud_suspected_with_both()
    {
        // Five approved purchases of 10 BRL in the last 60 s: this one is the sixth, and 100× their average.
        SeedApproved(count: 5, amount: 10m);

        await _handler.HandleAsync(_command with { Amount = 1000m });

        FraudSuspected suspected = Assert.IsType<FraudSuspected>(Assert.Single(_outbox.Saved).Event);
        Assert.Equal($"{VelocityRule.Code},{AmountAboveAverageRule.Code}", suspected.Rule);
    }

    [Fact]
    public async Task The_same_approval_again_writes_no_second_fraud_suspected()
    {
        SeedApproved(count: 4, amount: 40m);
        await _handler.HandleAsync(_command);

        Result<ApprovedPurchaseRecording> again = await _handler.HandleAsync(_command);

        Assert.Null(again.Value.Suspicion);
        Assert.Single(_outbox.Saved);
        Assert.Empty(_outbox.Added);
    }

    [Fact]
    public async Task The_event_id_is_the_same_for_the_same_authorization_and_differs_between_authorizations()
    {
        Assert.Equal(FraudSignals.EventIdOf(_command.AuthorizationId), FraudSignals.EventIdOf(_command.AuthorizationId));
        Assert.NotEqual(FraudSignals.EventIdOf(_command.AuthorizationId), FraudSignals.EventIdOf(Guid.CreateVersion7()));
        Assert.Equal(5, FraudSignals.EventIdOf(_command.AuthorizationId).Version);
    }

    private void SeedApproved(int count, decimal amount)
    {
        for (int index = 1; index <= count; index++)
        {
            _purchases.Seed(CardPurchase.Approve(
                Guid.CreateVersion7(),
                _command.CardId,
                Guid.NewGuid(),
                Money.Create(amount, "BRL").Value,
                AuthorizedAt.AddSeconds(-index)).Value);
        }
    }
}
