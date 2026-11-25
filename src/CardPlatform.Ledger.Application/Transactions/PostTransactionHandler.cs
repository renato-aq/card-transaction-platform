using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Accounts;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Transactions;

/// <summary>
/// Handles <see cref="PostTransaction"/>. Failures are checked in this order and the first one is returned, so bad
/// input is rejected before touching the database:
/// <list type="number">
/// <item><c>invalid_currency</c>;</item>
/// <item>each entry in order: <c>invalid_entry</c> (null), <c>invalid_account_id</c>, <c>invalid_direction</c>,
/// <c>invalid_amount</c> (missing) and <c>too_many_decimal_places</c>;</item>
/// <item>the domain rules of <see cref="LedgerTransaction.Create"/>: <c>insufficient_entries</c>, <c>invalid_amount</c>,
/// <c>unbalanced_transaction</c>;</item>
/// <item><c>account_not_found</c> for the first referenced account that does not exist;</item>
/// <item>the domain rules of <see cref="LedgerTransaction.ApplyTo"/>: <c>currency_mismatch</c>, <c>insufficient_funds</c>.</item>
/// </list>
/// The transaction is added and saved only after every check passed, so a failure writes nothing; a single
/// <see cref="IUnitOfWork.SaveChangesAsync"/> writes the transaction, its entries and the new balances atomically.
/// </summary>
public sealed class PostTransactionHandler(
    IAccountRepository accounts,
    ILedgerTransactionRepository transactions,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<PostTransaction, LedgerTransactionDto>
{
    public async Task<Result<LedgerTransactionDto>> HandleAsync(
        PostTransaction command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<Currency> currency = Currency.Create(command.Currency);
        if (currency.IsFailure)
        {
            return Result.Failure<LedgerTransactionDto>(currency.Error);
        }

        Result<List<Entry>> entries = BuildEntries(command.Entries ?? [], currency.Value);
        if (entries.IsFailure)
        {
            return Result.Failure<LedgerTransactionDto>(entries.Error);
        }

        Result<LedgerTransaction> transaction = LedgerTransaction.Create(
            entries.Value,
            timeProvider.GetUtcNow(),
            command.Description);
        if (transaction.IsFailure)
        {
            return Result.Failure<LedgerTransactionDto>(transaction.Error);
        }

        List<Guid> accountIds = [.. transaction.Value.Entries.Select(static entry => entry.AccountId).Distinct()];
        IReadOnlyList<Account> loadedAccounts = await accounts
            .GetByIdsAsync(accountIds, cancellationToken)
            .ConfigureAwait(false);

        Guid? missingAccountId = FindMissingAccount(accountIds, loadedAccounts);
        if (missingAccountId is not null)
        {
            return Result.Failure<LedgerTransactionDto>(AccountErrors.AccountNotFound(missingAccountId.Value));
        }

        Result applied = transaction.Value.ApplyTo(loadedAccounts);
        if (applied.IsFailure)
        {
            return Result.Failure<LedgerTransactionDto>(applied.Error);
        }

        transactions.Add(transaction.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToDto(transaction.Value));
    }

    private static Result<List<Entry>> BuildEntries(IReadOnlyList<PostTransactionEntry?> requested, Currency currency)
    {
        List<Entry> entries = new(requested.Count);
        for (int index = 0; index < requested.Count; index++)
        {
            PostTransactionEntry? entry = requested[index];
            if (entry is null)
            {
                return Result.Failure<List<Entry>>(TransactionErrors.InvalidEntry(index));
            }

            if (entry.AccountId is not { } accountId || accountId == Guid.Empty)
            {
                return Result.Failure<List<Entry>>(TransactionErrors.InvalidAccountId(index));
            }

            if (!TryParseDirection(entry.Direction, out EntryDirection direction))
            {
                return Result.Failure<List<Entry>>(TransactionErrors.InvalidDirection(index, entry.Direction));
            }

            if (entry.Amount is not { } amount)
            {
                return Result.Failure<List<Entry>>(TransactionErrors.MissingAmount(index));
            }

            Result<Money> money = Money.Create(amount, currency);
            if (money.IsFailure)
            {
                return Result.Failure<List<Entry>>(money.Error);
            }

            entries.Add(Entry.Create(accountId, direction, money.Value));
        }

        return Result.Success(entries);
    }

    // Matches the enum names only (case-insensitive). Enum.TryParse is not used because it also accepts
    // numeric strings such as "1", which are not valid directions for clients.
    private static bool TryParseDirection(string? value, out EntryDirection direction)
    {
        foreach (EntryDirection candidate in Enum.GetValues<EntryDirection>())
        {
            if (string.Equals(candidate.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                direction = candidate;
                return true;
            }
        }

        direction = default;
        return false;
    }

    private static Guid? FindMissingAccount(List<Guid> requestedIds, IReadOnlyList<Account> loadedAccounts)
    {
        HashSet<Guid> loadedIds = [.. loadedAccounts.Select(static account => account.Id)];
        foreach (Guid id in requestedIds)
        {
            if (!loadedIds.Contains(id))
            {
                return id;
            }
        }

        return null;
    }

    private static LedgerTransactionDto ToDto(LedgerTransaction transaction)
    {
        return new LedgerTransactionDto(
            transaction.Id,
            transaction.Description,
            transaction.Currency.Code,
            transaction.CreatedAt,
            [.. transaction.Entries.Select(static entry =>
                new EntryDto(entry.AccountId, entry.Direction, entry.Amount.Amount))]);
    }
}
