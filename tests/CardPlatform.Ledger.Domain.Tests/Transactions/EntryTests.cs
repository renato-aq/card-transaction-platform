using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Domain.Tests.Transactions;

public class EntryTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private static readonly Money TenReais = Money.Create(10m, "BRL").Value;

    [Fact]
    public void Debit_creates_a_debit_entry()
    {
        Entry entry = Entry.Debit(AccountId, TenReais);

        Assert.Equal(AccountId, entry.AccountId);
        Assert.Equal(EntryDirection.Debit, entry.Direction);
        Assert.Equal(TenReais, entry.Amount);
    }

    [Fact]
    public void Credit_creates_a_credit_entry()
    {
        Entry entry = Entry.Credit(AccountId, TenReais);

        Assert.Equal(AccountId, entry.AccountId);
        Assert.Equal(EntryDirection.Credit, entry.Direction);
        Assert.Equal(TenReais, entry.Amount);
    }

    [Fact]
    public void Entries_with_same_account_direction_and_amount_are_equal()
    {
        Assert.Equal(Entry.Debit(AccountId, TenReais), Entry.Create(AccountId, EntryDirection.Debit, TenReais));
    }

    [Fact]
    public void Empty_account_id_throws()
    {
        Assert.Throws<ArgumentException>(() => Entry.Debit(Guid.Empty, TenReais));
    }

    [Fact]
    public void Undefined_direction_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Entry.Create(AccountId, (EntryDirection)99, TenReais));
    }

    [Fact]
    public void Null_amount_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Entry.Credit(AccountId, null!));
    }
}
