using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Tests.Cards;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;
using CardPlatform.Contracts.Authorizations;

namespace CardPlatform.Authorization.Application.Tests.Authorizations;

public class VoidAuthorizationHandlerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = CreatedAt.AddHours(2);

    private readonly CardAuthorization _authorization;
    private readonly FakeCompletionLedgerClient _ledger = new();
    private readonly FakeAuthorizationRepository _authorizations;
    private readonly FakeOutbox _outbox = new();
    private readonly OutboxUnitOfWork _unitOfWork;

    public VoidAuthorizationHandlerTests()
    {
        _unitOfWork = new OutboxUnitOfWork(_outbox);
        Card card = Card.Create(Guid.NewGuid(), Currency.Create("BRL").Value, CreatedAt.AddDays(-1));
        _authorization = CardAuthorization.Approve(
            Guid.CreateVersion7(), card, Guid.NewGuid(), Money.Create(30m, "BRL").Value, CreatedAt);
        _authorizations = new FakeAuthorizationRepository(_authorization);
    }

    [Fact]
    public async Task Voids_the_hold_and_marks_the_authorization_voided()
    {
        Result<AuthorizationDto> result = await Void();

        Assert.True(result.IsSuccess);
        Assert.Equal([$"void {_authorization.Id}"], _ledger.Calls);
        Assert.Equal(AuthorizationStatus.Voided, _authorization.Status);
        Assert.Equal(Now, _authorization.CompletedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(AuthorizationStatus.Voided, result.Value.Status);
        Assert.Equal(Now, result.Value.CompletedAt);
    }

    [Fact]
    public async Task Void_is_saved_with_an_authorization_voided_event()
    {
        await Void();

        OutboxEntry entry = Assert.Single(_outbox.Added);
        Assert.Equal(
            new AuthorizationVoided(
                _authorization.Id, _authorization.CardId, _authorization.MerchantAccountId, 30m, "BRL", CreatedAt, Now),
            entry.Event);
        Assert.Equal(AuthorizationEvents.EventIdOf(_authorization.Id, typeof(AuthorizationVoided)), entry.EventId);
        Assert.Equal(Now, entry.OccurredAt);
        Assert.Equal([1], _unitOfWork.EventsAtSave);
    }

    [Fact]
    public async Task Unknown_authorization_fails_with_not_found_without_calling_the_ledger()
    {
        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(new VoidAuthorization(Guid.NewGuid()));

        AssertError(result, AuthorizationErrors.AuthorizationNotFoundCode, ErrorType.NotFound);
        Assert.Empty(_ledger.Calls);
    }

    [Theory]
    [InlineData(AuthorizationStatus.Captured)]
    [InlineData(AuthorizationStatus.Voided)]
    public async Task Completed_authorization_fails_with_not_voidable_without_calling_the_ledger(
        AuthorizationStatus status)
    {
        Assert.True((status == AuthorizationStatus.Captured
            ? _authorization.Capture(CreatedAt)
            : _authorization.Void(CreatedAt)).IsSuccess);

        Result<AuthorizationDto> result = await Void();

        AssertError(result, CardAuthorization.NotVoidableCode, ErrorType.BusinessRule);
        Assert.Empty(_ledger.Calls);
        Assert.Equal(status, _authorization.Status);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Declined_authorization_fails_with_not_voidable_without_calling_the_ledger()
    {
        Card card = Card.Create(Guid.NewGuid(), Currency.Create("BRL").Value, CreatedAt);
        CardAuthorization declined = CardAuthorization.Decline(
            Guid.CreateVersion7(), card, Guid.NewGuid(), Money.Create(30m, "BRL").Value, DeclineReason.CardBlocked, CreatedAt);
        _authorizations.Add(declined);

        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(new VoidAuthorization(declined.Id));

        AssertError(result, CardAuthorization.NotVoidableCode, ErrorType.BusinessRule);
        Assert.Empty(_ledger.Calls);
    }

    [Theory]
    [InlineData(LedgerErrors.UnavailableCode, ErrorType.Unavailable)]
    [InlineData(LedgerErrors.HoldNotFoundCode, ErrorType.NotFound)]
    [InlineData(LedgerErrors.ConcurrencyConflictCode, ErrorType.Conflict)]
    public async Task Ledger_failure_is_returned_as_it_is_and_leaves_the_authorization_approved(
        string code,
        ErrorType type)
    {
        _ledger.VoidError = new Error(code, "Ledger failure.", type);

        Result<AuthorizationDto> result = await Void();

        AssertError(result, code, type);
        Assert.Equal([$"void {_authorization.Id}"], _ledger.Calls);
        AssertUnchanged();
    }

    [Fact]
    public async Task Hold_already_voided_in_the_ledger_is_reconciled_as_a_successful_void()
    {
        _ledger.VoidError = HoldNotPending();
        _ledger.HoldStatus = LedgerHoldStatus.Voided;

        Result<AuthorizationDto> result = await Void();

        Assert.True(result.IsSuccess);
        Assert.Equal([$"void {_authorization.Id}", $"get {_authorization.Id}"], _ledger.Calls);
        Assert.Equal(AuthorizationStatus.Voided, result.Value.Status);
        Assert.Equal(AuthorizationStatus.Voided, _authorization.Status);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.IsType<AuthorizationVoided>(Assert.Single(_outbox.Added).Event);
        Assert.Equal([1], _unitOfWork.EventsAtSave);
    }

    [Fact]
    public async Task Hold_already_captured_in_the_ledger_marks_the_authorization_captured_and_fails_with_not_voidable()
    {
        _ledger.VoidError = HoldNotPending();
        _ledger.HoldStatus = LedgerHoldStatus.Captured;

        Result<AuthorizationDto> result = await Void();

        AssertError(result, CardAuthorization.NotVoidableCode, ErrorType.BusinessRule);
        Assert.Equal(AuthorizationStatus.Captured, _authorization.Status);
        Assert.Equal(Now, _authorization.CompletedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);

        // The event follows the status the authorization was saved with, not the operation that was asked for.
        OutboxEntry entry = Assert.Single(_outbox.Added);
        AuthorizationCaptured captured = Assert.IsType<AuthorizationCaptured>(entry.Event);
        Assert.Equal(Now, captured.CompletedAt);
        Assert.Equal(AuthorizationEvents.EventIdOf(_authorization.Id, typeof(AuthorizationCaptured)), entry.EventId);
        Assert.Equal([1], _unitOfWork.EventsAtSave);
    }

    [Fact]
    public async Task Hold_that_cannot_be_read_while_reconciling_returns_that_error_and_leaves_the_authorization_approved()
    {
        _ledger.VoidError = HoldNotPending();
        _ledger.GetHoldError = LedgerErrors.Unavailable();

        Result<AuthorizationDto> result = await Void();

        AssertError(result, LedgerErrors.UnavailableCode, ErrorType.Unavailable);
        AssertUnchanged();
    }

    [Fact]
    public async Task Hold_still_pending_while_reconciling_returns_hold_not_pending_and_leaves_the_authorization_approved()
    {
        _ledger.VoidError = HoldNotPending();
        _ledger.HoldStatus = LedgerHoldStatus.Pending;

        Result<AuthorizationDto> result = await Void();

        AssertError(result, LedgerErrors.HoldNotPendingCode, ErrorType.BusinessRule);
        AssertUnchanged();
    }

    private static Error HoldNotPending()
    {
        return Error.BusinessRule(LedgerErrors.HoldNotPendingCode, "The hold is not pending.");
    }

    private Task<Result<AuthorizationDto>> Void()
    {
        return CreateHandler().HandleAsync(new VoidAuthorization(_authorization.Id));
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

    private VoidAuthorizationHandler CreateHandler()
    {
        return new VoidAuthorizationHandler(_ledger, _authorizations, _outbox, _unitOfWork, new FixedTimeProvider(Now));
    }
}
