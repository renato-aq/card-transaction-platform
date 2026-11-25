using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Monetary;

namespace CardPlatform.Ledger.Domain.Tests.Accounts;

public class AccountTests
{
    private static readonly Currency Brl = Currency.Create("BRL").Value;

    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AccountType.Card)]
    [InlineData(AccountType.Merchant)]
    [InlineData(AccountType.Settlement)]
    public void Opened_account_has_type_currency_creation_date_and_zero_balance(AccountType type)
    {
        Account account = Account.Open(type, Brl, Now);

        Assert.Equal(type, account.Type);
        Assert.Equal(Brl, account.Currency);
        Assert.Equal(Now, account.CreatedAt);
        Assert.Equal(Money.Zero(Brl), account.Balance);
    }

    [Fact]
    public void Opened_account_gets_a_time_ordered_version_7_id()
    {
        Account account = Account.Open(AccountType.Card, Brl, Now);

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal(7, account.Id.Version);
    }

    [Fact]
    public void Each_opened_account_gets_a_distinct_id()
    {
        Account first = Account.Open(AccountType.Card, Brl, Now);
        Account second = Account.Open(AccountType.Card, Brl, Now);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Undefined_account_type_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Account.Open((AccountType)99, Brl, Now));
    }

    [Fact]
    public void Null_currency_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Account.Open(AccountType.Card, null!, Now));
    }
}
