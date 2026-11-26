using CardPlatform.Settlement.Job.ReadModel;
using CardPlatform.Settlement.Job.Seeding;

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

    /// <summary>The first instant of the settlement date, in UTC.</summary>
    public static DateTimeOffset DayStart(DateOnly date)
    {
        return new DateTimeOffset(date, TimeOnly.MinValue, TimeSpan.Zero);
    }
}
