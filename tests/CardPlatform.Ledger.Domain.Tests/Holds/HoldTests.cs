using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Domain.Tests.Holds;

public class HoldTests
{
    private static readonly Currency Brl = Currency.Create("BRL").Value;

    private static readonly Currency Usd = Currency.Create("USD").Value;

    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Later = Now.AddMinutes(5);

    private readonly Account _settlement = Account.Open(AccountType.Settlement, Brl, Now);

    private readonly Account _card = Account.Open(AccountType.Card, Brl, Now);

    private readonly Account _holds = Account.Open(AccountType.Holds, Brl, Now);

    private readonly Account _merchant = Account.Open(AccountType.Merchant, Brl, Now);

    public HoldTests()
    {
        Result topUp = LedgerTransaction.Create(
            [Entry.Debit(_settlement.Id, Reais(100m)), Entry.Credit(_card.Id, Reais(100m))],
            Now).Value.ApplyTo([_settlement, _card]);
        Assert.True(topUp.IsSuccess);
    }

    private static Money Reais(decimal amount)
    {
        return Money.Create(amount, Brl).Value;
    }

    private Hold PlaceHold(decimal amount)
    {
        Result<HoldPlacement> result = Hold.Place(Guid.NewGuid(), _card, _holds, Reais(amount), Now);
        Assert.True(result.IsSuccess);
        return result.Value.Hold;
    }

    private void AssertBalances(decimal card, decimal holds, decimal merchant)
    {
        Assert.Equal(Reais(card), _card.Balance);
        Assert.Equal(Reais(holds), _holds.Balance);
        Assert.Equal(Reais(merchant), _merchant.Balance);
        Assert.Equal(
            Money.Zero(Brl),
            _settlement.Balance + _card.Balance + _holds.Balance + _merchant.Balance);
    }

    private static void AssertTransfer(LedgerTransaction transaction, Account from, Account to, Money amount)
    {
        Assert.Equal([Entry.Debit(from.Id, amount), Entry.Credit(to.Id, amount)], transaction.Entries);
    }

    // ----- Place -----

    [Fact]
    public void Placing_a_hold_moves_the_amount_from_the_card_to_the_holds_account()
    {
        Guid id = Guid.NewGuid();

        Result<HoldPlacement> result = Hold.Place(id, _card, _holds, Reais(30m), Now);

        Assert.True(result.IsSuccess);
        Hold hold = result.Value.Hold;
        LedgerTransaction transaction = result.Value.Transaction;
        Assert.Equal(id, hold.Id);
        Assert.Equal(_card.Id, hold.CardAccountId);
        Assert.Equal(Reais(30m), hold.Amount);
        Assert.Equal(HoldStatus.Pending, hold.Status);
        Assert.Equal(Now, hold.CreatedAt);
        Assert.Null(hold.CompletedAt);
        Assert.Equal(transaction.Id, hold.PlacementTransactionId);
        Assert.Null(hold.CompletionTransactionId);
        Assert.Equal(Now, transaction.CreatedAt);
        AssertTransfer(transaction, _card, _holds, Reais(30m));
        AssertBalances(card: 70m, holds: 30m, merchant: 0m);
    }

    [Fact]
    public void Placing_a_hold_for_the_whole_card_balance_is_allowed()
    {
        PlaceHold(100m);

        AssertBalances(card: 0m, holds: 100m, merchant: 0m);
    }

    [Fact]
    public void Placing_a_hold_larger_than_the_card_balance_fails_with_insufficient_funds_and_changes_nothing()
    {
        Result<HoldPlacement> result = Hold.Place(Guid.NewGuid(), _card, _holds, Reais(100.01m), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(LedgerTransaction.InsufficientFundsCode, result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
        AssertBalances(card: 100m, holds: 0m, merchant: 0m);
    }

    [Fact]
    public void Placing_a_hold_in_another_currency_than_the_holds_account_fails_with_currency_mismatch()
    {
        Account usdHolds = Account.Open(AccountType.Holds, Usd, Now);

        Result<HoldPlacement> result = Hold.Place(Guid.NewGuid(), _card, usdHolds, Reais(10m), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(LedgerTransaction.CurrencyMismatchCode, result.Error.Code);
        AssertBalances(card: 100m, holds: 0m, merchant: 0m);
        Assert.Equal(Money.Zero(Usd), usdHolds.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Placing_a_hold_with_a_non_positive_amount_fails_with_invalid_amount(decimal amount)
    {
        Result<HoldPlacement> result = Hold.Place(Guid.NewGuid(), _card, _holds, Reais(amount), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(LedgerTransaction.InvalidAmountCode, result.Error.Code);
        AssertBalances(card: 100m, holds: 0m, merchant: 0m);
    }

    [Fact]
    public void Placing_a_hold_with_an_empty_id_throws()
    {
        Assert.Throws<ArgumentException>(() => Hold.Place(Guid.Empty, _card, _holds, Reais(10m), Now));
    }

    [Fact]
    public void Placing_a_hold_on_an_account_that_is_not_a_card_throws()
    {
        Assert.Throws<ArgumentException>(() => Hold.Place(Guid.NewGuid(), _merchant, _holds, Reais(10m), Now));
    }

    [Fact]
    public void Placing_a_hold_into_an_account_that_is_not_a_holds_account_throws()
    {
        Assert.Throws<ArgumentException>(() => Hold.Place(Guid.NewGuid(), _card, _merchant, Reais(10m), Now));
    }

    // ----- Capture -----

    [Fact]
    public void Capturing_a_hold_moves_the_amount_from_the_holds_account_to_the_merchant()
    {
        Hold hold = PlaceHold(30m);

        Result<LedgerTransaction> result = hold.Capture(_holds, _merchant, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(HoldStatus.Captured, hold.Status);
        Assert.Equal(Later, hold.CompletedAt);
        Assert.Equal(result.Value.Id, hold.CompletionTransactionId);
        Assert.Equal(Later, result.Value.CreatedAt);
        AssertTransfer(result.Value, _holds, _merchant, Reais(30m));
        AssertBalances(card: 70m, holds: 0m, merchant: 30m);
    }

    [Fact]
    public void Capturing_a_hold_twice_fails_with_hold_not_pending_and_changes_nothing()
    {
        Hold hold = PlaceHold(30m);
        Assert.True(hold.Capture(_holds, _merchant, Later).IsSuccess);
        Guid? completionTransactionId = hold.CompletionTransactionId;

        Result<LedgerTransaction> result = hold.Capture(_holds, _merchant, Later.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(Hold.HoldNotPendingCode, result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
        Assert.Equal(HoldStatus.Captured, hold.Status);
        Assert.Equal(Later, hold.CompletedAt);
        Assert.Equal(completionTransactionId, hold.CompletionTransactionId);
        AssertBalances(card: 70m, holds: 0m, merchant: 30m);
    }

    [Fact]
    public void Capturing_a_voided_hold_fails_with_hold_not_pending_and_changes_nothing()
    {
        Hold hold = PlaceHold(30m);
        Assert.True(hold.Void(_holds, _card, Later).IsSuccess);

        Result<LedgerTransaction> result = hold.Capture(_holds, _merchant, Later.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(Hold.HoldNotPendingCode, result.Error.Code);
        Assert.Equal(HoldStatus.Voided, hold.Status);
        AssertBalances(card: 100m, holds: 0m, merchant: 0m);
    }

    [Fact]
    public void Capturing_to_a_merchant_in_another_currency_fails_with_currency_mismatch_and_changes_nothing()
    {
        Hold hold = PlaceHold(30m);
        Account usdMerchant = Account.Open(AccountType.Merchant, Usd, Now);

        Result<LedgerTransaction> result = hold.Capture(_holds, usdMerchant, Later);

        Assert.True(result.IsFailure);
        Assert.Equal(LedgerTransaction.CurrencyMismatchCode, result.Error.Code);
        Assert.Equal(HoldStatus.Pending, hold.Status);
        Assert.Null(hold.CompletedAt);
        Assert.Null(hold.CompletionTransactionId);
        AssertBalances(card: 70m, holds: 30m, merchant: 0m);
        Assert.Equal(Money.Zero(Usd), usdMerchant.Balance);
    }

    [Fact]
    public void Capturing_from_a_holds_account_in_another_currency_fails_with_currency_mismatch()
    {
        Hold hold = PlaceHold(30m);
        Account usdHolds = Account.Open(AccountType.Holds, Usd, Now);

        Result<LedgerTransaction> result = hold.Capture(usdHolds, _merchant, Later);

        Assert.True(result.IsFailure);
        Assert.Equal(LedgerTransaction.CurrencyMismatchCode, result.Error.Code);
        Assert.Equal(HoldStatus.Pending, hold.Status);
        AssertBalances(card: 70m, holds: 30m, merchant: 0m);
    }

    [Theory]
    [InlineData(AccountType.Card)]
    [InlineData(AccountType.Settlement)]
    public void Capturing_to_an_account_that_is_not_a_merchant_fails_with_not_a_merchant_account(AccountType type)
    {
        Hold hold = PlaceHold(30m);
        Account notMerchant = Account.Open(type, Brl, Now);

        Result<LedgerTransaction> result = hold.Capture(_holds, notMerchant, Later);

        Assert.True(result.IsFailure);
        Assert.Equal(Hold.NotAMerchantAccountCode, result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
        Assert.Equal(HoldStatus.Pending, hold.Status);
        AssertBalances(card: 70m, holds: 30m, merchant: 0m);
    }

    [Fact]
    public void Capturing_from_an_account_that_is_not_a_holds_account_throws()
    {
        Hold hold = PlaceHold(30m);

        Assert.Throws<ArgumentException>(() => hold.Capture(_settlement, _merchant, Later));
        Assert.Equal(HoldStatus.Pending, hold.Status);
    }

    // ----- Void -----

    [Fact]
    public void Voiding_a_hold_returns_the_amount_from_the_holds_account_to_the_card()
    {
        Hold hold = PlaceHold(30m);

        Result<LedgerTransaction> result = hold.Void(_holds, _card, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(HoldStatus.Voided, hold.Status);
        Assert.Equal(Later, hold.CompletedAt);
        Assert.Equal(result.Value.Id, hold.CompletionTransactionId);
        AssertTransfer(result.Value, _holds, _card, Reais(30m));
        AssertBalances(card: 100m, holds: 0m, merchant: 0m);
    }

    [Fact]
    public void Voiding_a_captured_hold_fails_with_hold_not_pending_and_changes_nothing()
    {
        Hold hold = PlaceHold(30m);
        Assert.True(hold.Capture(_holds, _merchant, Later).IsSuccess);

        Result<LedgerTransaction> result = hold.Void(_holds, _card, Later.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(Hold.HoldNotPendingCode, result.Error.Code);
        Assert.Equal(HoldStatus.Captured, hold.Status);
        AssertBalances(card: 70m, holds: 0m, merchant: 30m);
    }

    [Fact]
    public void Voiding_a_hold_twice_fails_with_hold_not_pending()
    {
        Hold hold = PlaceHold(30m);
        Assert.True(hold.Void(_holds, _card, Later).IsSuccess);

        Result<LedgerTransaction> result = hold.Void(_holds, _card, Later.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal(Hold.HoldNotPendingCode, result.Error.Code);
        AssertBalances(card: 100m, holds: 0m, merchant: 0m);
    }

    [Fact]
    public void Voiding_from_a_holds_account_in_another_currency_fails_with_currency_mismatch()
    {
        Hold hold = PlaceHold(30m);
        Account usdHolds = Account.Open(AccountType.Holds, Usd, Now);

        Result<LedgerTransaction> result = hold.Void(usdHolds, _card, Later);

        Assert.True(result.IsFailure);
        Assert.Equal(LedgerTransaction.CurrencyMismatchCode, result.Error.Code);
        Assert.Equal(HoldStatus.Pending, hold.Status);
        AssertBalances(card: 70m, holds: 30m, merchant: 0m);
    }

    [Fact]
    public void Voiding_to_another_card_than_the_hold_card_throws()
    {
        Hold hold = PlaceHold(30m);
        Account otherCard = Account.Open(AccountType.Card, Brl, Now);

        Assert.Throws<ArgumentException>(() => hold.Void(_holds, otherCard, Later));
        Assert.Equal(HoldStatus.Pending, hold.Status);
    }

    // ----- Several holds -----

    [Fact]
    public void Several_holds_on_the_same_card_keep_the_ledger_balanced()
    {
        Hold captured = PlaceHold(40m);
        Hold voided = PlaceHold(25m);
        PlaceHold(10m);

        Assert.True(captured.Capture(_holds, _merchant, Later).IsSuccess);
        Assert.True(voided.Void(_holds, _card, Later).IsSuccess);

        AssertBalances(card: 50m, holds: 10m, merchant: 40m);
    }
}
