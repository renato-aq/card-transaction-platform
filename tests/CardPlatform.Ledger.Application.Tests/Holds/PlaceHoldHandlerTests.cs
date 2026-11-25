using CardPlatform.Ledger.Application.Holds;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Tests.Holds;

public class PlaceHoldHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Currency Brl = Currency.Create("BRL").Value;

    private readonly Account _settlement = Account.Open(AccountType.Settlement, Brl, Now);
    private readonly Account _card = Account.Open(AccountType.Card, Brl, Now);
    private readonly Account _merchant = Account.Open(AccountType.Merchant, Brl, Now);
    private readonly FakeAccountRepository _accounts;
    private readonly FakeHoldRepository _holds = new();
    private readonly FakeLedgerTransactionRepository _transactions = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly PlaceHoldHandler _handler;

    public PlaceHoldHandlerTests()
    {
        LedgerTransaction topUp = LedgerTransaction.Create(
            [Entry.Debit(_settlement.Id, Reais(100m)), Entry.Credit(_card.Id, Reais(100m))],
            Now).Value;
        Assert.True(topUp.ApplyTo([_settlement, _card]).IsSuccess);
        _accounts = new FakeAccountRepository(_settlement, _card, _merchant);
        _handler = new PlaceHoldHandler(_holds, _accounts, _transactions, _unitOfWork, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task First_hold_in_a_currency_opens_the_holds_account_and_saves_everything_once()
    {
        Guid id = Guid.CreateVersion7();

        Result<PlaceHoldResult> result = await _handler.HandleAsync(new PlaceHold(id, _card.Id, 30m, "BRL"));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Created);
        Assert.Equal(id, result.Value.Hold.Id);
        Assert.Equal(_card.Id, result.Value.Hold.CardAccountId);
        Assert.Equal(30m, result.Value.Hold.Amount.Amount);
        Assert.Equal("BRL", result.Value.Hold.Amount.Currency);
        Assert.Equal(HoldStatus.Pending, result.Value.Hold.Status);
        Assert.Equal(Now, result.Value.Hold.CreatedAt);
        Assert.Null(result.Value.Hold.CompletedAt);

        Account holdsAccount = Assert.Single(_accounts.Added);
        Assert.Equal(AccountType.Holds, holdsAccount.Type);
        Assert.Equal(Brl, holdsAccount.Currency);
        Assert.Equal(30m, holdsAccount.Balance.Amount);
        Assert.Equal(70m, _card.Balance.Amount);
        Hold hold = Assert.Single(_holds.Added);
        LedgerTransaction placement = Assert.Single(_transactions.Added);
        Assert.Equal(placement.Id, hold.PlacementTransactionId);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Created_at_is_recorded_and_returned_in_whole_microseconds()
    {
        // A Linux clock ticks every 100 ns; PostgreSQL keeps whole microseconds, so the response must match a later read.
        PlaceHoldHandler handler = new(_holds, _accounts, _transactions, _unitOfWork, new FixedTimeProvider(Now.AddTicks(7)));

        Result<PlaceHoldResult> result = await handler.HandleAsync(new PlaceHold(Guid.CreateVersion7(), _card.Id, 30m, "BRL"));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value.Hold.CreatedAt);
        Assert.Equal(Now, Assert.Single(_holds.Added).CreatedAt);
        Assert.Equal(Now, Assert.Single(_transactions.Added).CreatedAt);
        Assert.Equal(Now, Assert.Single(_accounts.Added).CreatedAt);
    }

    [Fact]
    public async Task Next_holds_reuse_the_holds_account_of_the_currency()
    {
        Result<PlaceHoldResult> first = await _handler.HandleAsync(new PlaceHold(Guid.CreateVersion7(), _card.Id, 30m, "BRL"));
        Result<PlaceHoldResult> second = await _handler.HandleAsync(new PlaceHold(Guid.CreateVersion7(), _card.Id, 20m, "BRL"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Account holdsAccount = Assert.Single(_accounts.Added);
        Assert.Equal(50m, holdsAccount.Balance.Amount);
        Assert.Equal(50m, _card.Balance.Amount);
    }

    [Fact]
    public async Task Repeating_the_same_hold_returns_it_without_writing()
    {
        Guid id = Guid.CreateVersion7();
        Assert.True((await _handler.HandleAsync(new PlaceHold(id, _card.Id, 30m, "BRL"))).IsSuccess);

        Result<PlaceHoldResult> repeated = await _handler.HandleAsync(new PlaceHold(id, _card.Id, 30.00m, "BRL"));

        Assert.True(repeated.IsSuccess);
        Assert.False(repeated.Value.Created);
        Assert.Equal(id, repeated.Value.Hold.Id);
        Assert.Single(_holds.Added);
        Assert.Single(_transactions.Added);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(70m, _card.Balance.Amount);
    }

    [Theory]
    [InlineData(31, "BRL", false)]
    [InlineData(30, "USD", false)]
    [InlineData(30, "BRL", true)]
    public async Task Repeating_the_hold_id_with_different_data_is_a_conflict(int amount, string currency, bool otherCard)
    {
        Guid id = Guid.CreateVersion7();
        Assert.True((await _handler.HandleAsync(new PlaceHold(id, _card.Id, 30m, "BRL"))).IsSuccess);
        Guid cardAccountId = otherCard ? Guid.CreateVersion7() : _card.Id;

        Result<PlaceHoldResult> result = await _handler.HandleAsync(new PlaceHold(id, cardAccountId, amount, currency));

        Assert.True(result.IsFailure);
        Assert.Equal(HoldErrors.HoldIdConflictCode, result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(_transactions.Added);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(null, "0195b5a4-0000-7000-8000-000000000001", 10.0, "BRL", "invalid_hold_id")]
    [InlineData("00000000-0000-0000-0000-000000000000", "0195b5a4-0000-7000-8000-000000000001", 10.0, "BRL", "invalid_hold_id")]
    [InlineData("0195b5a4-0000-7000-8000-0000000000a1", null, 10.0, "BRL", "invalid_account_id")]
    [InlineData("0195b5a4-0000-7000-8000-0000000000a1", "00000000-0000-0000-0000-000000000000", 10.0, "BRL", "invalid_account_id")]
    [InlineData("0195b5a4-0000-7000-8000-0000000000a1", "0195b5a4-0000-7000-8000-000000000001", 10.0, null, "invalid_currency")]
    [InlineData("0195b5a4-0000-7000-8000-0000000000a1", "0195b5a4-0000-7000-8000-000000000001", 10.0, "brl", "invalid_currency")]
    [InlineData("0195b5a4-0000-7000-8000-0000000000a1", "0195b5a4-0000-7000-8000-000000000001", null, "BRL", "invalid_amount")]
    [InlineData("0195b5a4-0000-7000-8000-0000000000a1", "0195b5a4-0000-7000-8000-000000000001", 0.0, "BRL", "invalid_amount")]
    [InlineData("0195b5a4-0000-7000-8000-0000000000a1", "0195b5a4-0000-7000-8000-000000000001", -1.0, "BRL", "invalid_amount")]
    [InlineData("0195b5a4-0000-7000-8000-0000000000a1", "0195b5a4-0000-7000-8000-000000000001", 10.001, "BRL", "too_many_decimal_places")]
    public async Task Invalid_input_is_rejected_before_touching_the_repositories(
        string? id,
        string? cardAccountId,
        double? amount,
        string? currency,
        string expectedCode)
    {
        Result<PlaceHoldResult> result = await _handler.HandleAsync(new PlaceHold(
            id is null ? null : Guid.Parse(id),
            cardAccountId is null ? null : Guid.Parse(cardAccountId),
            (decimal?)amount,
            currency));

        AssertRejected(result, expectedCode, ErrorType.Validation);
    }

    [Fact]
    public async Task Unknown_card_account_is_not_found()
    {
        Result<PlaceHoldResult> result = await _handler.HandleAsync(
            new PlaceHold(Guid.CreateVersion7(), Guid.CreateVersion7(), 10m, "BRL"));

        AssertRejected(result, "account_not_found", ErrorType.NotFound);
    }

    [Fact]
    public async Task Account_that_is_not_a_card_is_rejected_without_throwing()
    {
        Result<PlaceHoldResult> result = await _handler.HandleAsync(
            new PlaceHold(Guid.CreateVersion7(), _merchant.Id, 10m, "BRL"));

        AssertRejected(result, HoldErrors.NotACardAccountCode, ErrorType.BusinessRule);
    }

    [Fact]
    public async Task Hold_above_the_card_balance_is_rejected_as_insufficient_funds()
    {
        Result<PlaceHoldResult> result = await _handler.HandleAsync(
            new PlaceHold(Guid.CreateVersion7(), _card.Id, 100.01m, "BRL"));

        AssertRejected(result, LedgerTransaction.InsufficientFundsCode, ErrorType.BusinessRule);
        Assert.Equal(100m, _card.Balance.Amount);
    }

    [Fact]
    public async Task Hold_in_another_currency_than_the_card_is_rejected_as_currency_mismatch()
    {
        Result<PlaceHoldResult> result = await _handler.HandleAsync(
            new PlaceHold(Guid.CreateVersion7(), _card.Id, 10m, "USD"));

        AssertRejected(result, LedgerTransaction.CurrencyMismatchCode, ErrorType.BusinessRule);
        Assert.Equal(100m, _card.Balance.Amount);
    }

    private void AssertRejected(Result<PlaceHoldResult> result, string expectedCode, ErrorType expectedType)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(expectedType, result.Error.Type);
        Assert.Empty(_holds.Added);
        Assert.Empty(_transactions.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private static Money Reais(decimal amount)
    {
        return Money.Create(amount, Brl).Value;
    }
}
