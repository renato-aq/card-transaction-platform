using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Monetary;

namespace CardPlatform.Ledger.Domain.Accounts;

/// <summary>
/// Ledger account holding a balance in a single currency. The balance follows the convention
/// <c>credits − debits</c> and only changes when a <see cref="Transactions.LedgerTransaction"/> is applied.
/// </summary>
public sealed class Account
{
    private Account(Guid id, AccountType type, Currency currency, DateTimeOffset createdAt)
    {
        Id = id;
        Type = type;
        Currency = currency;
        Balance = Money.Zero(currency);
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public AccountType Type { get; }

    public Currency Currency { get; }

    public Money Balance { get; private set; }

    /// <summary>When the account was opened, in whole microseconds (see <see cref="Timestamps"/>).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Opens an account with a zero balance. The id is a time-ordered UUID v7 derived from
    /// <paramref name="createdAt"/>. Opening cannot fail for a business reason: the currency is already
    /// validated, so an undefined <paramref name="type"/> or a <see langword="null"/> currency is a programming error.
    /// </summary>
    public static Account Open(AccountType type, Currency currency, DateTimeOffset createdAt)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown account type.");
        }

        ArgumentNullException.ThrowIfNull(currency);

        DateTimeOffset recordedAt = createdAt.TruncateToMicroseconds();
        return new Account(Guid.CreateVersion7(recordedAt), type, currency, recordedAt);
    }

    /// <summary>
    /// Whether a balance is allowed for this account type: <see cref="AccountType.Card"/> accounts cannot be negative.
    /// </summary>
    internal bool AllowsBalance(Money balance)
    {
        return Type != AccountType.Card || balance >= Money.Zero(Currency);
    }

    /// <summary>
    /// Adds the net effect (<c>credits − debits</c>) of a transaction to the balance. Only called by
    /// <see cref="Transactions.LedgerTransaction.ApplyTo"/> after every invariant has been checked.
    /// </summary>
    internal void ApplyNetChange(Money netChange)
    {
        Balance += netChange;
    }
}
