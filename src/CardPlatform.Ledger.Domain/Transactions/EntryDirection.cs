namespace CardPlatform.Ledger.Domain.Transactions;

/// <summary>
/// Side of an entry. With the convention <c>balance = credits − debits</c>, a debit decreases the
/// account balance and a credit increases it.
/// </summary>
public enum EntryDirection
{
    Debit,
    Credit,
}
