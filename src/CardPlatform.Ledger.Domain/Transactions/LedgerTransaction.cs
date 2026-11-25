using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Monetary;

namespace CardPlatform.Ledger.Domain.Transactions;

/// <summary>
/// Balanced set of entries moving money between accounts (double-entry bookkeeping).
/// A transaction that exists is always valid: at least two entries, all positive, all in the same
/// currency, and the sum of debits equals the sum of credits.
/// </summary>
public sealed class LedgerTransaction
{
    public const string InsufficientEntriesCode = "insufficient_entries";

    public const string InvalidAmountCode = "invalid_amount";

    public const string MixedCurrenciesCode = "mixed_currencies";

    public const string UnbalancedTransactionCode = "unbalanced_transaction";

    public const string CurrencyMismatchCode = "currency_mismatch";

    public const string InsufficientFundsCode = "insufficient_funds";

    public const int MinimumEntries = 2;

    private readonly List<Entry> _entries;

    private LedgerTransaction(Guid id, string? description, DateTimeOffset createdAt, List<Entry> entries)
    {
        Id = id;
        Description = description;
        CreatedAt = createdAt;
        _entries = entries;
    }

    // Used only to rehydrate persisted transactions: the entries are loaded into the list afterwards.
    private LedgerTransaction(Guid id, string? description, DateTimeOffset createdAt)
        : this(id, description, createdAt, [])
    {
    }

    public Guid Id { get; }

    public string? Description { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<Entry> Entries => _entries.AsReadOnly();

    /// <summary>The currency shared by every entry.</summary>
    public Currency Currency => _entries[0].Amount.Currency;

    /// <summary>
    /// Creates a transaction. The id is a time-ordered UUID v7 derived from <paramref name="createdAt"/>;
    /// a blank <paramref name="description"/> is stored as <see langword="null"/>.
    /// Rules are checked in this order and the first violation is returned:
    /// <c>insufficient_entries</c>, <c>invalid_amount</c>, <c>mixed_currencies</c>, <c>unbalanced_transaction</c>.
    /// </summary>
    public static Result<LedgerTransaction> Create(
        IEnumerable<Entry> entries,
        DateTimeOffset createdAt,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        List<Entry> entryList = [.. entries];
        if (entryList.Contains(null!))
        {
            throw new ArgumentException("Entries must not contain null.", nameof(entries));
        }

        Error? error = Validate(entryList);
        if (error is not null)
        {
            return Result.Failure<LedgerTransaction>(error);
        }

        string? normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return Result.Success(new LedgerTransaction(
            Guid.CreateVersion7(createdAt),
            normalizedDescription,
            createdAt,
            entryList));
    }

    /// <summary>
    /// Applies the net effect (<c>credits − debits</c>) of this transaction to the accounts it references,
    /// all or nothing. Every rule is checked for every account before any balance changes:
    /// first <c>currency_mismatch</c> for all accounts, then <c>insufficient_funds</c> (a <see cref="AccountType.Card"/>
    /// account cannot end up negative); accounts are checked in the order they first appear in the entries.
    /// </summary>
    /// <param name="accounts">
    /// The accounts referenced by the entries. Accounts not referenced are ignored. A referenced account that is
    /// missing, or the same account id given twice, is a programming error (the caller loads the accounts and
    /// reports unknown accounts itself) and throws <see cref="ArgumentException"/>.
    /// </param>
    public Result ApplyTo(IEnumerable<Account> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        Dictionary<Guid, Account> accountsById = IndexById(accounts);
        List<(Guid AccountId, Money NetChange)> netChanges = NetChangeByAccount();

        foreach ((Guid accountId, _) in netChanges)
        {
            if (!accountsById.ContainsKey(accountId))
            {
                throw new ArgumentException(
                    $"Account {accountId} referenced by transaction {Id} was not provided.",
                    nameof(accounts));
            }
        }

        foreach ((Guid accountId, _) in netChanges)
        {
            Account account = accountsById[accountId];
            if (account.Currency != Currency)
            {
                return Result.Failure(Error.BusinessRule(
                    CurrencyMismatchCode,
                    $"Account {account.Id} is in {account.Currency.Code} but the transaction is in {Currency.Code}."));
            }
        }

        foreach ((Guid accountId, Money netChange) in netChanges)
        {
            Account account = accountsById[accountId];
            if (!account.AllowsBalance(account.Balance + netChange))
            {
                return Result.Failure(Error.BusinessRule(
                    InsufficientFundsCode,
                    $"Account {account.Id} has a balance of {account.Balance} and cannot be debited {Money.Zero(Currency) - netChange}."));
            }
        }

        foreach ((Guid accountId, Money netChange) in netChanges)
        {
            accountsById[accountId].ApplyNetChange(netChange);
        }

        return Result.Success();
    }

    private static Error? Validate(List<Entry> entries)
    {
        if (entries.Count < MinimumEntries)
        {
            return Error.Validation(
                InsufficientEntriesCode,
                $"A transaction needs at least {MinimumEntries} entries, but {entries.Count} were given.");
        }

        Entry? nonPositive = entries.Find(static entry => entry.Amount.Amount <= 0m);
        if (nonPositive is not null)
        {
            return Error.Validation(
                InvalidAmountCode,
                $"Entry amounts must be greater than zero, but account {nonPositive.AccountId} has {nonPositive.Amount}.");
        }

        Currency currency = entries[0].Amount.Currency;
        if (entries.Exists(entry => entry.Amount.Currency != currency))
        {
            return Error.BusinessRule(MixedCurrenciesCode, "All entries of a transaction must be in the same currency.");
        }

        Money debits = Sum(entries, EntryDirection.Debit, currency);
        Money credits = Sum(entries, EntryDirection.Credit, currency);
        if (debits != credits)
        {
            return Error.BusinessRule(
                UnbalancedTransactionCode,
                $"Debits ({debits}) must equal credits ({credits}).");
        }

        return null;
    }

    private static Money Sum(List<Entry> entries, EntryDirection direction, Currency currency)
    {
        Money total = Money.Zero(currency);
        foreach (Entry entry in entries)
        {
            if (entry.Direction == direction)
            {
                total += entry.Amount;
            }
        }

        return total;
    }

    private static Dictionary<Guid, Account> IndexById(IEnumerable<Account> accounts)
    {
        Dictionary<Guid, Account> accountsById = [];
        foreach (Account account in accounts)
        {
            ArgumentNullException.ThrowIfNull(account, nameof(accounts));
            if (!accountsById.TryAdd(account.Id, account))
            {
                throw new ArgumentException($"Account {account.Id} was provided more than once.", nameof(accounts));
            }
        }

        return accountsById;
    }

    // Ordered by first appearance in the entries, so the reported error is deterministic when several
    // accounts break a rule. An account that appears in several entries gets its net effect.
    private List<(Guid AccountId, Money NetChange)> NetChangeByAccount()
    {
        List<(Guid AccountId, Money NetChange)> netChanges = [];
        Dictionary<Guid, int> positionByAccount = [];
        foreach (Entry entry in _entries)
        {
            Money signedAmount = entry.SignedAmount();
            if (positionByAccount.TryGetValue(entry.AccountId, out int position))
            {
                netChanges[position] = (entry.AccountId, netChanges[position].NetChange + signedAmount);
            }
            else
            {
                positionByAccount.Add(entry.AccountId, netChanges.Count);
                netChanges.Add((entry.AccountId, signedAmount));
            }
        }

        return netChanges;
    }
}
