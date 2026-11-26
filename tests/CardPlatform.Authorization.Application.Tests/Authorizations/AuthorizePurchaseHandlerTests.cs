using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Application.Tests.Cards;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;
using CardPlatform.Contracts.Authorizations;

using DeclineReasons = CardPlatform.Authorization.Application.Authorizations.DeclineReasons;

namespace CardPlatform.Authorization.Application.Tests.Authorizations;

public class AuthorizePurchaseHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private readonly Card _card = Card.Create(Guid.NewGuid(), Currency.Create("BRL").Value, Now.AddDays(-1));
    private readonly Guid _merchantAccountId = Guid.NewGuid();
    private readonly FakeHoldLedgerClient _ledger = new();
    private readonly FakeAuthorizationRepository _authorizations = new();
    private readonly FakeOutbox _outbox = new();
    private readonly OutboxUnitOfWork _unitOfWork;

    public AuthorizePurchaseHandlerTests()
    {
        _unitOfWork = new OutboxUnitOfWork(_outbox);
    }

    [Fact]
    public async Task Approves_and_places_the_hold_with_the_authorization_id()
    {
        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(Purchase(30m, "BRL"));

        Assert.True(result.IsSuccess);
        CardAuthorization saved = Assert.Single(_authorizations.Added);
        Assert.Equal(AuthorizationStatus.Approved, saved.Status);
        Assert.Null(saved.DeclineReason);
        Assert.Equal(7, saved.Id.Version);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
        PlacedHold hold = Assert.Single(_ledger.PlacedHolds);
        Assert.Equal(new PlacedHold(saved.Id, _card.LedgerAccountId, 30m, "BRL"), hold);
        Assert.Equal(
            new AuthorizationDto(saved.Id, AuthorizationStatus.Approved, null, _card.Id, _merchantAccountId, 30.00m, "BRL", Now),
            result.Value);
    }

    [Fact]
    public async Task Approved_authorization_is_saved_with_an_authorization_approved_event()
    {
        Result<AuthorizationDto> result = await CreateHandler(new FixedTimeProvider(Now.AddTicks(7)))
            .HandleAsync(Purchase(30m, "BRL"));

        Guid id = result.Value.Id;
        OutboxEntry entry = Assert.Single(_outbox.Added);
        Assert.Equal(new AuthorizationApproved(id, _card.Id, _merchantAccountId, 30m, "BRL", Now), entry.Event);
        Assert.Equal(AuthorizationEvents.EventIdOf(id, typeof(AuthorizationApproved)), entry.EventId);
        Assert.Equal(Now, entry.OccurredAt);
        Assert.Equal([1], _unitOfWork.EventsAtSave);
    }

    [Theory]
    [InlineData(false, DeclineReasons.CardBlockedCode)]
    [InlineData(true, DeclineReasons.InsufficientFundsCode)]
    public async Task Declined_authorization_is_saved_with_an_authorization_declined_event_and_its_reason(
        bool refusedByTheLedger,
        string reason)
    {
        if (refusedByTheLedger)
        {
            _ledger.PlaceHoldError = Error.BusinessRule(LedgerErrors.InsufficientFundsCode, "Insufficient funds.");
        }
        else
        {
            _card.Block();
        }

        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(Purchase(30m, "BRL"));

        Guid id = result.Value.Id;
        OutboxEntry entry = Assert.Single(_outbox.Added);
        Assert.Equal(new AuthorizationDeclined(id, _card.Id, _merchantAccountId, 30m, "BRL", Now, reason), entry.Event);
        Assert.Equal(AuthorizationEvents.EventIdOf(id, typeof(AuthorizationDeclined)), entry.EventId);
        Assert.Equal(Now, entry.OccurredAt);
        Assert.Equal([1], _unitOfWork.EventsAtSave);
    }

    [Fact]
    public async Task Created_at_is_recorded_and_returned_in_whole_microseconds()
    {
        // A Linux clock ticks every 100 ns; PostgreSQL keeps whole microseconds, so the response must match a later read.
        Result<AuthorizationDto> result = await CreateHandler(new FixedTimeProvider(Now.AddTicks(7)))
            .HandleAsync(Purchase(30m, "BRL"));

        Assert.Equal(Now, Assert.Single(_authorizations.Added).CreatedAt);
        Assert.Equal(Now, result.Value.CreatedAt);
    }

    [Fact]
    public async Task Given_authorization_id_is_used_for_the_authorization_and_the_hold()
    {
        Guid reservedId = Guid.CreateVersion7();

        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(Purchase(30m, "BRL") with { AuthorizationId = reservedId });

        Assert.Equal(reservedId, result.Value.Id);
        Assert.Equal(reservedId, Assert.Single(_authorizations.Added).Id);
        Assert.Equal(reservedId, Assert.Single(_ledger.PlacedHolds).HoldId);
    }

    [Fact]
    public async Task Blocked_card_is_declined_without_calling_the_ledger()
    {
        _card.Block();

        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(Purchase(30m, "BRL"));

        AssertDeclined(result, DeclineReason.CardBlocked, DeclineReasons.CardBlockedCode);
        Assert.Empty(_ledger.PlacedHolds);
    }

    [Fact]
    public async Task Currency_other_than_the_card_currency_is_declined_without_calling_the_ledger()
    {
        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(Purchase(30m, "USD"));

        AssertDeclined(result, DeclineReason.CurrencyMismatch, DeclineReasons.CurrencyMismatchCode);
        Assert.Empty(_ledger.PlacedHolds);
    }

    [Fact]
    public async Task Blocked_card_is_checked_before_the_currency()
    {
        _card.Block();

        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(Purchase(30m, "USD"));

        AssertDeclined(result, DeclineReason.CardBlocked, DeclineReasons.CardBlockedCode);
    }

    [Fact]
    public async Task Insufficient_funds_in_the_ledger_is_declined_with_the_attempted_hold_id()
    {
        _ledger.PlaceHoldError = Error.BusinessRule(LedgerErrors.InsufficientFundsCode, "Insufficient funds.");

        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(Purchase(30m, "BRL"));

        AssertDeclined(result, DeclineReason.InsufficientFunds, DeclineReasons.InsufficientFundsCode);
        Assert.Equal(result.Value.Id, Assert.Single(_ledger.PlacedHolds).HoldId);
    }

    [Fact]
    public async Task Ledger_unavailable_is_returned_and_nothing_is_saved()
    {
        _ledger.PlaceHoldError = LedgerErrors.Unavailable();

        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(Purchase(30m, "BRL"));

        Assert.True(result.IsFailure);
        Assert.Equal(LedgerErrors.UnavailableCode, result.Error.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        AssertNothingSaved();
    }

    [Theory]
    [InlineData(LedgerErrors.HoldIdConflictCode, ErrorType.Conflict)]
    [InlineData(LedgerErrors.AccountNotFoundCode, ErrorType.NotFound)]
    [InlineData(LedgerErrors.CurrencyMismatchCode, ErrorType.BusinessRule)]
    [InlineData(LedgerErrors.InsufficientFundsCode, ErrorType.Validation)]
    public async Task Other_ledger_errors_are_returned_as_they_are_and_nothing_is_saved(string code, ErrorType type)
    {
        _ledger.PlaceHoldError = new Error(code, "Ledger error.", type);

        Result<AuthorizationDto> result = await CreateHandler().HandleAsync(Purchase(30m, "BRL"));

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(type, result.Error.Type);
        AssertNothingSaved();
    }

    [Fact]
    public async Task Unknown_card_fails_with_card_not_found()
    {
        Result<AuthorizationDto> result = await CreateHandler()
            .HandleAsync(new AuthorizePurchase(Guid.NewGuid(), _merchantAccountId, 30m, "BRL"));

        Assert.True(result.IsFailure);
        Assert.Equal(CardErrors.CardNotFoundCode, result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_ledger.PlacedHolds);
        AssertNothingSaved();
    }

    public static TheoryData<AuthorizePurchaseCase, string> InvalidInputs()
    {
        Guid card = Guid.NewGuid();
        Guid merchant = Guid.NewGuid();
        return new TheoryData<AuthorizePurchaseCase, string>
        {
            { new(null, merchant, 10m, "BRL"), AuthorizationErrors.InvalidCardIdCode },
            { new(Guid.Empty, merchant, 10m, "BRL"), AuthorizationErrors.InvalidCardIdCode },
            { new(card, null, 10m, "BRL"), AuthorizationErrors.InvalidMerchantAccountIdCode },
            { new(card, Guid.Empty, 10m, "BRL"), AuthorizationErrors.InvalidMerchantAccountIdCode },
            { new(card, merchant, 10m, null), Currency.InvalidCurrencyCode },
            { new(card, merchant, 10m, "brl"), Currency.InvalidCurrencyCode },
            { new(card, merchant, null, "BRL"), AuthorizationErrors.InvalidAmountCode },
            { new(card, merchant, 0m, "BRL"), AuthorizationErrors.InvalidAmountCode },
            { new(card, merchant, -5m, "BRL"), AuthorizationErrors.InvalidAmountCode },
            { new(card, merchant, 10.001m, "BRL"), Money.TooManyDecimalPlacesCode },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task Invalid_input_fails_validation_before_reading_the_card(AuthorizePurchaseCase input, string code)
    {
        ArgumentNullException.ThrowIfNull(input);

        Result<AuthorizationDto> result = await CreateHandler()
            .HandleAsync(new AuthorizePurchase(input.CardId, input.MerchantAccountId, input.Amount, input.Currency));

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_ledger.PlacedHolds);
        AssertNothingSaved();
    }

    private AuthorizePurchase Purchase(decimal amount, string currency)
    {
        return new AuthorizePurchase(_card.Id, _merchantAccountId, amount, currency);
    }

    private void AssertDeclined(Result<AuthorizationDto> result, DeclineReason reason, string code)
    {
        Assert.True(result.IsSuccess);
        CardAuthorization saved = Assert.Single(_authorizations.Added);
        Assert.Equal(AuthorizationStatus.Declined, saved.Status);
        Assert.Equal(reason, saved.DeclineReason);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(saved.Id, result.Value.Id);
        Assert.Equal(AuthorizationStatus.Declined, result.Value.Status);
        Assert.Equal(code, result.Value.DeclineReason);
    }

    private void AssertNothingSaved()
    {
        Assert.Empty(_authorizations.Added);
        Assert.Empty(_outbox.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private AuthorizePurchaseHandler CreateHandler(TimeProvider? timeProvider = null)
    {
        return new AuthorizePurchaseHandler(
            _ledger,
            new FakeCardRepository(_card),
            _authorizations,
            _outbox,
            _unitOfWork,
            timeProvider ?? new FixedTimeProvider(Now));
    }
}

/// <summary>Raw input of an <see cref="AuthorizePurchase"/>, serializable for xUnit theory data.</summary>
public sealed record AuthorizePurchaseCase(Guid? CardId, Guid? MerchantAccountId, decimal? Amount, string? Currency);
