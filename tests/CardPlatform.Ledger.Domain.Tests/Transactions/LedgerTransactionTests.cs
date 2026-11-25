using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Domain.Tests.Transactions;

public class LedgerTransactionTests
{
    private static readonly Currency Brl = Currency.Create("BRL").Value;

    private static readonly Currency Usd = Currency.Create("USD").Value;

    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private static readonly Guid FirstAccountId = Guid.NewGuid();

    private static readonly Guid SecondAccountId = Guid.NewGuid();

    private static Money Reais(decimal amount)
    {
        return Money.Create(amount, Brl).Value;
    }

    private static Money Dollars(decimal amount)
    {
        return Money.Create(amount, Usd).Value;
    }

    private static Account Open(AccountType type)
    {
        return Account.Open(type, Brl, Now);
    }

    private static LedgerTransaction Transaction(params Entry[] entries)
    {
        return LedgerTransaction.Create(entries, Now).Value;
    }

    private static void TopUp(Account settlement, Account card, decimal amount)
    {
        Result result = Transaction(Entry.Debit(settlement.Id, Reais(amount)), Entry.Credit(card.Id, Reais(amount)))
            .ApplyTo([settlement, card]);
        Assert.True(result.IsSuccess);
    }

    // ----- Create -----

    [Fact]
    public void Balanced_transaction_is_created_with_its_data()
    {
        Entry debit = Entry.Debit(FirstAccountId, Reais(10m));
        Entry credit = Entry.Credit(SecondAccountId, Reais(10m));

        Result<LedgerTransaction> result = LedgerTransaction.Create([debit, credit], Now, "Purchase at bakery");

        Assert.True(result.IsSuccess);
        LedgerTransaction transaction = result.Value;
        Assert.Equal(7, transaction.Id.Version);
        Assert.Equal("Purchase at bakery", transaction.Description);
        Assert.Equal(Now, transaction.CreatedAt);
        Assert.Equal([debit, credit], transaction.Entries);
        Assert.Equal(Brl, transaction.Currency);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_description_is_stored_as_null(string? description)
    {
        Result<LedgerTransaction> result = LedgerTransaction.Create(
            [Entry.Debit(FirstAccountId, Reais(10m)), Entry.Credit(SecondAccountId, Reais(10m))],
            Now,
            description);

        Assert.Null(result.Value.Description);
    }

    [Fact]
    public void Transaction_with_several_debits_and_credits_that_balance_is_created()
    {
        Guid thirdAccountId = Guid.NewGuid();

        Result<LedgerTransaction> result = LedgerTransaction.Create(
            [
                Entry.Debit(FirstAccountId, Reais(100m)),
                Entry.Credit(SecondAccountId, Reais(60m)),
                Entry.Credit(thirdAccountId, Reais(40m)),
            ],
            Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Entries.Count);
    }

    [Fact]
    public void Entries_are_copied_so_later_changes_to_the_source_do_not_affect_the_transaction()
    {
        List<Entry> entries = [Entry.Debit(FirstAccountId, Reais(10m)), Entry.Credit(SecondAccountId, Reais(10m))];

        LedgerTransaction transaction = LedgerTransaction.Create(entries, Now).Value;
        entries.Clear();

        Assert.Equal(2, transaction.Entries.Count);
    }

    [Fact]
    public void Transaction_without_entries_is_rejected()
    {
        Result<LedgerTransaction> result = LedgerTransaction.Create([], Now);

        Assert.True(result.IsFailure);
        Assert.Equal("insufficient_entries", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Transaction_with_a_single_entry_is_rejected()
    {
        Result<LedgerTransaction> result = LedgerTransaction.Create([Entry.Debit(FirstAccountId, Reais(10m))], Now);

        Assert.True(result.IsFailure);
        Assert.Equal("insufficient_entries", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Entry_with_zero_or_negative_amount_is_rejected(decimal amount)
    {
        Result<LedgerTransaction> result = LedgerTransaction.Create(
            [Entry.Debit(FirstAccountId, Reais(amount)), Entry.Credit(SecondAccountId, Reais(amount))],
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_amount", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Entries_in_different_currencies_are_rejected()
    {
        Result<LedgerTransaction> result = LedgerTransaction.Create(
            [Entry.Debit(FirstAccountId, Reais(10m)), Entry.Credit(SecondAccountId, Dollars(10m))],
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("mixed_currencies", result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
    }

    [Fact]
    public void Transaction_whose_debits_differ_from_its_credits_is_rejected()
    {
        Result<LedgerTransaction> result = LedgerTransaction.Create(
            [Entry.Debit(FirstAccountId, Reais(10m)), Entry.Credit(SecondAccountId, Reais(9.99m))],
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("unbalanced_transaction", result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
    }

    [Fact]
    public void Transaction_with_only_debits_is_rejected_as_unbalanced()
    {
        Result<LedgerTransaction> result = LedgerTransaction.Create(
            [Entry.Debit(FirstAccountId, Reais(10m)), Entry.Debit(SecondAccountId, Reais(10m))],
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("unbalanced_transaction", result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
    }

    [Fact]
    public void Insufficient_entries_is_reported_before_invalid_amount()
    {
        Result<LedgerTransaction> result = LedgerTransaction.Create([Entry.Debit(FirstAccountId, Reais(0m))], Now);

        Assert.Equal("insufficient_entries", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Invalid_amount_is_reported_before_mixed_currencies()
    {
        Result<LedgerTransaction> result = LedgerTransaction.Create(
            [Entry.Debit(FirstAccountId, Reais(-10m)), Entry.Credit(SecondAccountId, Dollars(10m))],
            Now);

        Assert.Equal("invalid_amount", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Mixed_currencies_is_reported_before_unbalanced_transaction()
    {
        Result<LedgerTransaction> result = LedgerTransaction.Create(
            [Entry.Debit(FirstAccountId, Reais(10m)), Entry.Credit(SecondAccountId, Dollars(5m))],
            Now);

        Assert.Equal("mixed_currencies", result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
    }

    [Fact]
    public void Null_entries_throw()
    {
        Assert.Throws<ArgumentNullException>(() => LedgerTransaction.Create(null!, Now));
    }

    [Fact]
    public void Null_entry_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            LedgerTransaction.Create([Entry.Debit(FirstAccountId, Reais(10m)), null!], Now));
    }

    // ----- ApplyTo -----

    [Fact]
    public void Top_up_debits_settlement_and_credits_card()
    {
        Account settlement = Open(AccountType.Settlement);
        Account card = Open(AccountType.Card);
        LedgerTransaction topUp = Transaction(Entry.Debit(settlement.Id, Reais(100m)), Entry.Credit(card.Id, Reais(100m)));

        Result result = topUp.ApplyTo([settlement, card]);

        Assert.True(result.IsSuccess);
        Assert.Equal(Reais(-100m), settlement.Balance);
        Assert.Equal(Reais(100m), card.Balance);
    }

    [Fact]
    public void Purchase_debits_card_and_credits_merchant()
    {
        Account settlement = Open(AccountType.Settlement);
        Account card = Open(AccountType.Card);
        Account merchant = Open(AccountType.Merchant);
        TopUp(settlement, card, 100m);
        LedgerTransaction purchase = Transaction(Entry.Debit(card.Id, Reais(30m)), Entry.Credit(merchant.Id, Reais(30m)));

        Result result = purchase.ApplyTo([card, merchant]);

        Assert.True(result.IsSuccess);
        Assert.Equal(Reais(70m), card.Balance);
        Assert.Equal(Reais(30m), merchant.Balance);
    }

    [Fact]
    public void Settlement_debits_merchant_and_credits_settlement_and_all_balances_sum_to_zero()
    {
        Account settlement = Open(AccountType.Settlement);
        Account card = Open(AccountType.Card);
        Account merchant = Open(AccountType.Merchant);
        TopUp(settlement, card, 100m);
        Assert.True(Transaction(Entry.Debit(card.Id, Reais(30m)), Entry.Credit(merchant.Id, Reais(30m)))
            .ApplyTo([card, merchant]).IsSuccess);
        LedgerTransaction settle = Transaction(Entry.Debit(merchant.Id, Reais(30m)), Entry.Credit(settlement.Id, Reais(30m)));

        Result result = settle.ApplyTo([merchant, settlement]);

        Assert.True(result.IsSuccess);
        Assert.Equal(Reais(0m), merchant.Balance);
        Assert.Equal(Reais(-70m), settlement.Balance);
        Assert.Equal(Reais(70m), card.Balance);
        Assert.Equal(Money.Zero(Brl), settlement.Balance + card.Balance + merchant.Balance);
    }

    [Fact]
    public void Card_can_be_debited_down_to_exactly_zero()
    {
        Account settlement = Open(AccountType.Settlement);
        Account card = Open(AccountType.Card);
        Account merchant = Open(AccountType.Merchant);
        TopUp(settlement, card, 50m);

        Result result = Transaction(Entry.Debit(card.Id, Reais(50m)), Entry.Credit(merchant.Id, Reais(50m)))
            .ApplyTo([card, merchant]);

        Assert.True(result.IsSuccess);
        Assert.Equal(Money.Zero(Brl), card.Balance);
    }

    [Fact]
    public void Purchase_above_card_balance_is_rejected_and_no_balance_changes()
    {
        Account settlement = Open(AccountType.Settlement);
        Account card = Open(AccountType.Card);
        Account merchant = Open(AccountType.Merchant);
        TopUp(settlement, card, 50m);
        LedgerTransaction purchase = Transaction(Entry.Debit(card.Id, Reais(50.01m)), Entry.Credit(merchant.Id, Reais(50.01m)));

        Result result = purchase.ApplyTo([card, merchant]);

        Assert.True(result.IsFailure);
        Assert.Equal("insufficient_funds", result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
        Assert.Equal(Reais(50m), card.Balance);
        Assert.Equal(Money.Zero(Brl), merchant.Balance);
    }

    [Fact]
    public void Insufficient_funds_on_a_later_card_leaves_earlier_accounts_unchanged()
    {
        Account settlement = Open(AccountType.Settlement);
        Account fundedCard = Open(AccountType.Card);
        Account emptyCard = Open(AccountType.Card);
        Account merchant = Open(AccountType.Merchant);
        TopUp(settlement, fundedCard, 100m);
        LedgerTransaction transaction = Transaction(
            Entry.Debit(fundedCard.Id, Reais(10m)),
            Entry.Debit(emptyCard.Id, Reais(10m)),
            Entry.Credit(merchant.Id, Reais(20m)));

        Result result = transaction.ApplyTo([fundedCard, emptyCard, merchant]);

        Assert.Equal("insufficient_funds", result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
        Assert.Equal(Reais(100m), fundedCard.Balance);
        Assert.Equal(Money.Zero(Brl), emptyCard.Balance);
        Assert.Equal(Money.Zero(Brl), merchant.Balance);
    }

    [Theory]
    [InlineData(AccountType.Merchant)]
    [InlineData(AccountType.Settlement)]
    public void Merchant_and_settlement_accounts_can_become_negative(AccountType type)
    {
        Account debited = Open(type);
        Account credited = Open(AccountType.Card);

        Result result = Transaction(Entry.Debit(debited.Id, Reais(25m)), Entry.Credit(credited.Id, Reais(25m)))
            .ApplyTo([debited, credited]);

        Assert.True(result.IsSuccess);
        Assert.Equal(Reais(-25m), debited.Balance);
        Assert.Equal(Reais(25m), credited.Balance);
    }

    [Fact]
    public void Account_in_another_currency_is_rejected_and_no_balance_changes()
    {
        Account settlement = Open(AccountType.Settlement);
        Account dollarCard = Account.Open(AccountType.Card, Usd, Now);
        LedgerTransaction transaction = Transaction(
            Entry.Debit(settlement.Id, Reais(10m)),
            Entry.Credit(dollarCard.Id, Reais(10m)));

        Result result = transaction.ApplyTo([settlement, dollarCard]);

        Assert.True(result.IsFailure);
        Assert.Equal("currency_mismatch", result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
        Assert.Equal(Money.Zero(Brl), settlement.Balance);
        Assert.Equal(Money.Zero(Usd), dollarCard.Balance);
    }

    [Fact]
    public void Currency_mismatch_is_reported_before_insufficient_funds()
    {
        Account emptyCard = Open(AccountType.Card);
        Account dollarMerchant = Account.Open(AccountType.Merchant, Usd, Now);
        LedgerTransaction transaction = Transaction(
            Entry.Debit(emptyCard.Id, Reais(10m)),
            Entry.Credit(dollarMerchant.Id, Reais(10m)));

        Result result = transaction.ApplyTo([emptyCard, dollarMerchant]);

        Assert.Equal("currency_mismatch", result.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, result.Error.Type);
    }

    [Fact]
    public void Account_in_several_entries_receives_the_net_effect()
    {
        Account settlement = Open(AccountType.Settlement);
        Account card = Open(AccountType.Card);
        Account merchant = Open(AccountType.Merchant);
        TopUp(settlement, card, 20m);
        // Gross debit of 50 exceeds the balance of 20, but the net effect on the card is only -10.
        LedgerTransaction transaction = Transaction(
            Entry.Debit(card.Id, Reais(50m)),
            Entry.Credit(card.Id, Reais(40m)),
            Entry.Credit(merchant.Id, Reais(10m)));

        Result result = transaction.ApplyTo([card, merchant]);

        Assert.True(result.IsSuccess);
        Assert.Equal(Reais(10m), card.Balance);
        Assert.Equal(Reais(10m), merchant.Balance);
    }

    [Fact]
    public void Accounts_not_referenced_by_the_entries_are_ignored()
    {
        Account settlement = Open(AccountType.Settlement);
        Account card = Open(AccountType.Card);
        Account unrelated = Open(AccountType.Merchant);

        Result result = Transaction(Entry.Debit(settlement.Id, Reais(10m)), Entry.Credit(card.Id, Reais(10m)))
            .ApplyTo([settlement, card, unrelated]);

        Assert.True(result.IsSuccess);
        Assert.Equal(Money.Zero(Brl), unrelated.Balance);
    }

    [Fact]
    public void Missing_referenced_account_throws_and_no_balance_changes()
    {
        Account settlement = Open(AccountType.Settlement);
        Account card = Open(AccountType.Card);
        LedgerTransaction topUp = Transaction(Entry.Debit(settlement.Id, Reais(10m)), Entry.Credit(card.Id, Reais(10m)));

        Assert.Throws<ArgumentException>(() => topUp.ApplyTo([settlement]));
        Assert.Equal(Money.Zero(Brl), settlement.Balance);
    }

    [Fact]
    public void Same_account_provided_twice_throws()
    {
        Account settlement = Open(AccountType.Settlement);
        Account card = Open(AccountType.Card);
        LedgerTransaction topUp = Transaction(Entry.Debit(settlement.Id, Reais(10m)), Entry.Credit(card.Id, Reais(10m)));

        Assert.Throws<ArgumentException>(() => topUp.ApplyTo([settlement, card, card]));
    }

    [Fact]
    public void Null_accounts_throw()
    {
        LedgerTransaction topUp = Transaction(Entry.Debit(FirstAccountId, Reais(10m)), Entry.Credit(SecondAccountId, Reais(10m)));

        Assert.Throws<ArgumentNullException>(() => topUp.ApplyTo(null!));
    }
}
