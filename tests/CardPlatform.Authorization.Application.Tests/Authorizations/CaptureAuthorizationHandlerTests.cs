using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Tests.Cards;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;
using CardPlatform.Contracts.Authorizations;

namespace CardPlatform.Authorization.Application.Tests.Authorizations;

public class CaptureAuthorizationHandlerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = CreatedAt.AddHours(2);

    private readonly Guid _merchantAccountId = Guid.NewGuid();
    private readonly CardAuthorization _authorization;
    private readonly FakeCompletionLedgerClient _ledger = new();
    private readonly FakeAuthorizationRepository _authorizations;
    private readonly FakeOutbox _outbox = new();
    private readonly OutboxUnitOfWork _unitOfWork;

    public CaptureAuthorizationHandlerTests()
    {
        _unitOfWork = new OutboxUnitOfWork(_outbox);
        Card card = Card.Create(Guid.NewGuid(), Currency.Create("BRL").Value, CreatedAt.AddDays(-1));
        _authorization = CardAuthorization.Approve(
            Guid.CreateVersion7(), card, _merchantAccountId, Money.Create(30m, "BRL").Value, CreatedAt);
        _authorizations = new FakeAuthorizationRepository(_authorization);
    }

    [Fact]
    public async Task Captures_the_hold_to_the_merchant_and_marks_the_authorization_captured()
    {
        Result<AuthorizationDto> result = await Capture();

        Assert.True(result.IsSuccess);
        Assert.Equal([$"capture {_authorization.Id}"], _ledger.Calls);
        Assert.Equal(_merchantAccountId, _ledger.CapturedTo);
        Assert.Equal(AuthorizationStatus.Captured, _authorization.Status);
        Assert.Equal(Now, _authorization.CompletedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(AuthorizationStatus.Captured, result.Value.Status);
        Assert.Equal(Now, result.Value.CompletedAt);
        Assert.Equal(_authorization.Id, result.Value.Id);
    }

    [Fact]
    public async Task Capture_is_saved_with_an_authorization_captured_event()
    {
        await Capture();

        OutboxEntry entry = Assert.Single(_outbox.Added);
        Assert.Equal(
            new AuthorizationCaptured(_authorization.Id, _authorization.CardId, _merchantAccountId, 30m, "BRL", CreatedAt, Now),
            entry.Event);
        Assert.Equal(AuthorizationEvents.EventIdOf(_authorization.Id, typeof(AuthorizationCaptured)), entry.EventId);
        Assert.Equal(Now, entry.OccurredAt);
        Assert.Equal([1], _unitOfWork.EventsAtSave);
    }

    [Fact]
    public async Task Concurrent_captures_that_both_save_the_status_write_events_with_the_same_id()
    {
        // Two requests loaded the authorization while it was approved: the first captures the hold, the second gets
        // hold_not_pending and reconciles to the same status (ADR 0007). Both save it, so both write the event.
        Card card = Card.Create(_authorization.CardId, Currency.Create("BRL").Value, CreatedAt.AddDays(-1));
        CardAuthorization sameAuthorization = CardAuthorization.Approve(
            _authorization.Id, card, _merchantAccountId, Money.Create(30m, "BRL").Value, CreatedAt);
        FakeCompletionLedgerClient reconcilingLedger = new()
        {
            CaptureError = HoldNotPending(),
            HoldStatus = LedgerHoldStatus.Captured,
        };
        FakeOutbox otherOutbox = new();

        await Capture();
        Result<AuthorizationDto> second = await new CaptureAuthorizationHandler(
                reconcilingLedger,
                new FakeAuthorizationRepository(sameAuthorization),
                otherOutbox,
                new OutboxUnitOfWork(otherOutbox),
                new FixedTimeProvider(Now.AddSeconds(1)))
            .HandleAsync(new CaptureAuthorization(_authorization.Id));

        Assert.True(second.IsSuccess);
        Assert.Equal(Assert.Single(_outbox.Added).EventId, Assert.Single(otherOutbox.Added).EventId);
    }

    [Fact]
    public async Task Completed_at_is_recorded_and_returned_in_whole_microseconds()
    {
        // A Linux clock ticks every 100 ns; PostgreSQL keeps whole microseconds, so the response must match a later read.
        Result<AuthorizationDto> result = await CreateHandler(new FixedTimeProvider(Now.AddTicks(7)))
            .HandleAsync(new CaptureAuthorization(_authorization.Id));

        Assert.Equal(Now, _authorization.CompletedAt);
        Assert.Equal(Now, result.Value.CompletedAt);
    }

    [Fact]
    public async Task Unknown_authorization_fails_with_not_found_without_calling_the_ledger()
    {
        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(new CaptureAuthorization(Guid.NewGuid()));

        Assert.Equal(AuthorizationErrors.AuthorizationNotFoundCode, result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_ledger.Calls);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(AuthorizationStatus.Captured)]
    [InlineData(AuthorizationStatus.Voided)]
    public async Task Completed_authorization_fails_with_not_capturable_without_calling_the_ledger(
        AuthorizationStatus status)
    {
        Assert.True((status == AuthorizationStatus.Captured
            ? _authorization.Capture(CreatedAt)
            : _authorization.Void(CreatedAt)).IsSuccess);

        Result<AuthorizationDto> result = await Capture();

        AssertError(result, CardAuthorization.NotCapturableCode, ErrorType.BusinessRule);
        Assert.Empty(_ledger.Calls);
        Assert.Equal(status, _authorization.Status);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Declined_authorization_fails_with_not_capturable_without_calling_the_ledger()
    {
        Card card = Card.Create(Guid.NewGuid(), Currency.Create("BRL").Value, CreatedAt);
        CardAuthorization declined = CardAuthorization.Decline(
            Guid.CreateVersion7(), card, _merchantAccountId, Money.Create(30m, "BRL").Value, DeclineReason.InsufficientFunds, CreatedAt);
        _authorizations.Add(declined);

        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(new CaptureAuthorization(declined.Id));

        AssertError(result, CardAuthorization.NotCapturableCode, ErrorType.BusinessRule);
        Assert.Empty(_ledger.Calls);
        Assert.Equal(AuthorizationStatus.Declined, declined.Status);
    }

    [Theory]
    [InlineData(LedgerErrors.UnavailableCode, ErrorType.Unavailable)]
    [InlineData(LedgerErrors.NotAMerchantAccountCode, ErrorType.BusinessRule)]
    [InlineData(LedgerErrors.CurrencyMismatchCode, ErrorType.BusinessRule)]
    [InlineData(LedgerErrors.AccountNotFoundCode, ErrorType.NotFound)]
    [InlineData(LedgerErrors.ConcurrencyConflictCode, ErrorType.Conflict)]
    public async Task Ledger_failure_is_returned_as_it_is_and_leaves_the_authorization_approved(
        string code,
        ErrorType type)
    {
        _ledger.CaptureError = new Error(code, "Ledger failure.", type);

        Result<AuthorizationDto> result = await Capture();

        AssertError(result, code, type);
        Assert.Equal([$"capture {_authorization.Id}"], _ledger.Calls);
        AssertUnchanged();
    }

    [Fact]
    public async Task Hold_already_captured_in_the_ledger_is_reconciled_as_a_successful_capture()
    {
        _ledger.CaptureError = HoldNotPending();
        _ledger.HoldStatus = LedgerHoldStatus.Captured;

        Result<AuthorizationDto> result = await Capture();

        Assert.True(result.IsSuccess);
        Assert.Equal([$"capture {_authorization.Id}", $"get {_authorization.Id}"], _ledger.Calls);
        Assert.Equal(AuthorizationStatus.Captured, result.Value.Status);
        Assert.Equal(AuthorizationStatus.Captured, _authorization.Status);
        Assert.Equal(Now, _authorization.CompletedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.IsType<AuthorizationCaptured>(Assert.Single(_outbox.Added).Event);
        Assert.Equal([1], _unitOfWork.EventsAtSave);
    }

    [Fact]
    public async Task Hold_already_voided_in_the_ledger_marks_the_authorization_voided_and_fails_with_not_capturable()
    {
        _ledger.CaptureError = HoldNotPending();
        _ledger.HoldStatus = LedgerHoldStatus.Voided;

        Result<AuthorizationDto> result = await Capture();

        AssertError(result, CardAuthorization.NotCapturableCode, ErrorType.BusinessRule);
        Assert.Equal(AuthorizationStatus.Voided, _authorization.Status);
        Assert.Equal(Now, _authorization.CompletedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);

        // The event follows the status the authorization was saved with, not the operation that was asked for.
        OutboxEntry entry = Assert.Single(_outbox.Added);
        Assert.Equal(
            new AuthorizationVoided(_authorization.Id, _authorization.CardId, _merchantAccountId, 30m, "BRL", CreatedAt, Now),
            entry.Event);
        Assert.Equal(AuthorizationEvents.EventIdOf(_authorization.Id, typeof(AuthorizationVoided)), entry.EventId);
        Assert.Equal([1], _unitOfWork.EventsAtSave);
    }

    [Fact]
    public async Task Hold_that_cannot_be_read_while_reconciling_returns_that_error_and_leaves_the_authorization_approved()
    {
        _ledger.CaptureError = HoldNotPending();
        _ledger.GetHoldError = LedgerErrors.Unavailable();

        Result<AuthorizationDto> result = await Capture();

        AssertError(result, LedgerErrors.UnavailableCode, ErrorType.Unavailable);
        AssertUnchanged();
    }

    [Fact]
    public async Task Hold_still_pending_while_reconciling_returns_hold_not_pending_and_leaves_the_authorization_approved()
    {
        _ledger.CaptureError = HoldNotPending();
        _ledger.HoldStatus = LedgerHoldStatus.Pending;

        Result<AuthorizationDto> result = await Capture();

        AssertError(result, LedgerErrors.HoldNotPendingCode, ErrorType.BusinessRule);
        AssertUnchanged();
    }

    private static Error HoldNotPending()
    {
        return Error.BusinessRule(LedgerErrors.HoldNotPendingCode, "The hold is not pending.");
    }

    private Task<Result<AuthorizationDto>> Capture()
    {
        return CreateHandler().HandleAsync(new CaptureAuthorization(_authorization.Id));
    }

    private void AssertUnchanged()
    {
        Assert.Equal(AuthorizationStatus.Approved, _authorization.Status);
        Assert.Null(_authorization.CompletedAt);
        Assert.Equal(0, _unitOfWork.SaveCount);
        Assert.Empty(_outbox.Added);
    }

    private static void AssertError(Result<AuthorizationDto> result, string code, ErrorType type)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(type, result.Error.Type);
    }

    private CaptureAuthorizationHandler CreateHandler(TimeProvider? timeProvider = null)
    {
        return new CaptureAuthorizationHandler(
            _ledger, _authorizations, _outbox, _unitOfWork, timeProvider ?? new FixedTimeProvider(Now));
    }
}
