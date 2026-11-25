using CardPlatform.Ledger.Domain.Monetary;

namespace CardPlatform.Ledger.Domain.Transactions;

/// <summary>
/// One leg of a <see cref="LedgerTransaction"/>: an amount debited from or credited to an account.
/// The amount is validated (positive, same currency, balanced) by <see cref="LedgerTransaction.Create"/>,
/// since an entry only has meaning inside a transaction.
/// </summary>
public sealed record Entry
{
    private Entry(Guid accountId, EntryDirection direction, Money amount)
    {
        AccountId = accountId;
        Direction = direction;
        Amount = amount;
    }

    public Guid AccountId { get; }

    public EntryDirection Direction { get; }

    public Money Amount { get; }

    public static Entry Debit(Guid accountId, Money amount)
    {
        return Create(accountId, EntryDirection.Debit, amount);
    }

    public static Entry Credit(Guid accountId, Money amount)
    {
        return Create(accountId, EntryDirection.Credit, amount);
    }

    public static Entry Create(Guid accountId, EntryDirection direction, Money amount)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("Account id must not be empty.", nameof(accountId));
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown entry direction.");
        }

        ArgumentNullException.ThrowIfNull(amount);

        return new Entry(accountId, direction, amount);
    }

    /// <summary>
    /// Effect of this entry on the account balance: <c>+amount</c> for a credit, <c>−amount</c> for a debit.
    /// </summary>
    internal Money SignedAmount()
    {
        return Direction == EntryDirection.Credit
            ? Amount
            : Money.Zero(Amount.Currency) - Amount;
    }
}
