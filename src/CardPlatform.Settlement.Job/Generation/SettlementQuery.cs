using CardPlatform.Settlement.Job.ReadModel;
using CardPlatform.Settlement.Job.Seeding;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// What is settled, shared by every strategy: each credit entry on a <c>Merchant</c> account, in the requested currency,
/// whose transaction was created within the UTC day of the settlement date, the half-open interval
/// [date 00:00, next day 00:00).
/// </summary>
/// <remarks>
/// The order of the details is a contract that every strategy must reproduce: merchant account id, then transaction
/// id, then entry id. It is applied by the database (<c>ORDER BY</c>), never in memory, because PostgreSQL compares
/// <c>uuid</c> values by their bytes and the file must follow that order.
/// </remarks>
public static class SettlementQuery
{
    /// <summary>The settlement entries of <paramref name="options"/>, in the order of the file.</summary>
    public static IQueryable<EntryRecord> Entries(LedgerReadDbContext dbContext, RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);

        DateTimeOffset dayStart = DayStart(options.Date);
        DateTimeOffset nextDayStart = dayStart.AddDays(1);
        string currency = options.Currency;

        return dbContext.Entries
            .Where(entry => entry.Direction == LedgerSchema.Credit
                && entry.Currency == currency
                && entry.Account.Type == LedgerSchema.MerchantAccount
                && entry.Transaction.CreatedAt >= dayStart
                && entry.Transaction.CreatedAt < nextDayStart)
            .OrderBy(entry => entry.AccountId)
            .ThenBy(entry => entry.TransactionId)
            .ThenBy(entry => entry.Id);
    }

    /// <summary>The merchant accounts, whose credits are settled.</summary>
    public static IQueryable<AccountRecord> MerchantAccounts(LedgerReadDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return dbContext.Accounts.Where(account => account.Type == LedgerSchema.MerchantAccount);
    }

    /// <summary>
    /// The same selection and order as <see cref="Entries"/>, restricted to one merchant account (one of
    /// <see cref="MerchantAccounts"/>) and, when <paramref name="after"/> is given, to the entries after that key within
    /// the merchant, compared by the database as a row value: <c>(transaction_id, id) &gt; (…)</c>; at most
    /// <paramref name="limit"/> entries. The rows are projections, never tracked.
    /// </summary>
    /// <remarks>
    /// Written in SQL because its shape decides what a page costs, and EF Core does not produce it from LINQ (it turns the
    /// per-entry lookup below into a join over every transaction). The account is matched by equality and the key by a
    /// row value on the two columns that follow it in the index <c>ix_entries_account_id_transaction_id_id</c>, so
    /// PostgreSQL uses both as the start of the index scan: a page starts reading at the key, wherever it is inside the
    /// merchant, and stops at the limit. (A row value on the whole key, <c>(account_id, transaction_id, id) &gt; (…)</c>,
    /// next to the equality makes the scan start at the merchant's first entry and walk to the key on every page.) The
    /// transaction of each entry is looked up by its primary key (<c>CROSS JOIN LATERAL … LIMIT 1</c>, which PostgreSQL
    /// cannot turn into a hash or merge join that reads the day's transactions from their start). The values are
    /// parameters.
    /// </remarks>
    public static IQueryable<SettlementRow> MerchantEntries(
        LedgerReadDbContext dbContext,
        RunOptions options,
        Guid merchantAccountId,
        SettlementKey? after,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        DateTimeOffset dayStart = DayStart(options.Date);
        DateTimeOffset nextDayStart = dayStart.AddDays(1);

        // The first page of a merchant has no key to start after; every other page starts after the last key read, which
        // is a key within the merchant (transaction id, entry id).
        FormattableString sql;
        if (after is { } key)
        {
            sql = $"""
               SELECT e.account_id AS "AccountId", e.transaction_id AS "TransactionId", e.id AS "EntryId",
                      e.amount AS "Amount", t.created_at AS "TransactedAt"
               FROM entries AS e
               CROSS JOIN LATERAL (
                   SELECT tr.created_at FROM transactions AS tr WHERE tr.id = e.transaction_id LIMIT 1) AS t
               WHERE e.account_id = {merchantAccountId} AND e.direction = {LedgerSchema.Credit}
                 AND e.currency = CAST({options.Currency} AS char(3))
                 AND t.created_at >= {dayStart} AND t.created_at < {nextDayStart}
                 AND (e.transaction_id, e.id) > ({key.TransactionId}, {key.EntryId})
               ORDER BY e.account_id, e.transaction_id, e.id
               LIMIT {limit}
               """;
        }
        else
        {
            sql = $"""
               SELECT e.account_id AS "AccountId", e.transaction_id AS "TransactionId", e.id AS "EntryId",
                      e.amount AS "Amount", t.created_at AS "TransactedAt"
               FROM entries AS e
               CROSS JOIN LATERAL (
                   SELECT tr.created_at FROM transactions AS tr WHERE tr.id = e.transaction_id LIMIT 1) AS t
               WHERE e.account_id = {merchantAccountId} AND e.direction = {LedgerSchema.Credit}
                 AND e.currency = CAST({options.Currency} AS char(3))
                 AND t.created_at >= {dayStart} AND t.created_at < {nextDayStart}
               ORDER BY e.account_id, e.transaction_id, e.id
               LIMIT {limit}
               """;
        }

        return dbContext.Database.SqlQuery<SettlementRow>(sql);
    }

    /// <summary>The first instant of the settlement date, in UTC.</summary>
    public static DateTimeOffset DayStart(DateOnly date)
    {
        return new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero);
    }
}
