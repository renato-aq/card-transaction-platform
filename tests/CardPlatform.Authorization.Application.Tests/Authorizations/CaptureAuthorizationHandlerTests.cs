using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Tests.Cards;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Application.Tests.Authorizations;

public class CaptureAuthorizationHandlerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Now = CreatedAt.AddHours(2);

    private readonly Guid _merchantAccountId = Guid.NewGuid();
    private readonly CardAuthorization _authorization;
    private readonly FakeCompletionLedgerClient _ledger = new();
    private readonly FakeAuthorizationRepository _authorizations;
    private readonly FakeUnitOfWork _unitOfWork = new();

    public CaptureAuthorizationHandlerTests()
    {
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
    }

    private static void AssertError(Result<AuthorizationDto> result, string code, ErrorType type)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(type, result.Error.Type);
    }

    private CaptureAuthorizationHandler CreateHandler()
    {
        return new CaptureAuthorizationHandler(_ledger, _authorizations, _unitOfWork, new FixedTimeProvider(Now));
    }
}
