using System.Globalization;

namespace CardPlatform.Settlement.Job.Tests.Infrastructure;

/// <summary>
/// A known set of ledger rows around 2026-11-20 (UTC): five merchant credits in BRL within the day, including both
/// ends of the day, and rows that must be left out of its file.
/// </summary>
internal static class KnownDay
{
    public static readonly Guid Merchant1 = Id("10000000-0000-0000-0000-000000000001");

    public static readonly Guid Merchant2 = Id("20000000-0000-0000-0000-000000000002");

    public static readonly Guid DollarMerchant = Id("30000000-0000-0000-0000-000000000003");

    public static readonly Guid Card = Id("c0000000-0000-0000-0000-000000000001");

    public static readonly Guid DollarCard = Id("c0000000-0000-0000-0000-000000000002");

    public static readonly Guid SettlementAccount = Id("50000000-0000-0000-0000-000000000001");

    // Within the day. The ids do not follow the instants, so the file order is by id, not by time.
    public static readonly Guid LastInstantPurchase = Id("03000000-0000-0000-0000-000000000003");

    public static readonly Guid FirstInstantPurchase = Id("a2000000-0000-0000-0000-000000000002");

    public static readonly Guid MorningPurchase = Id("a1000000-0000-0000-0000-000000000001");

    public static readonly Guid SplitPurchase = Id("a4000000-0000-0000-0000-000000000004");

    // Left out of the file.
    public static readonly Guid PreviousDayPurchase = Id("e1000000-0000-0000-0000-000000000001");

    public static readonly Guid NextDayPurchase = Id("e2000000-0000-0000-0000-000000000002");

    public static readonly Guid DollarPurchase = Id("e3000000-0000-0000-0000-000000000003");

    public static readonly Guid CardTopUp = Id("e4000000-0000-0000-0000-000000000004");

    public static readonly Guid MerchantPayout = Id("e5000000-0000-0000-0000-000000000005");

    public static async Task ArrangeAsync(string connectionString)
    {
        await AddAccountAsync(connectionString, Merchant2, "Merchant", "BRL");
        await AddAccountAsync(connectionString, Merchant1, "Merchant", "BRL");
        await AddAccountAsync(connectionString, DollarMerchant, "Merchant", "USD");
        await AddAccountAsync(connectionString, Card, "Card", "BRL");
        await AddAccountAsync(connectionString, DollarCard, "Card", "USD");
        await AddAccountAsync(connectionString, SettlementAccount, "Settlement", "BRL");

        // Inserted in an order that differs from the file's at every level, so the entry ids (insertion order) do
        // not give the file order: the later merchant first, and within each merchant the higher transaction id
        // first. Only the two credits of the split purchase are in entry id order, which is the file order.
        await AddTransactionAsync(
            connectionString, SplitPurchase, At(2026, 11, 20, 12, 0, 0, 0),
            (Card, "Debit", 30.00m, "BRL"), (Merchant2, "Credit", 20.00m, "BRL"), (Merchant2, "Credit", 10.00m, "BRL"));
        await AddTransactionAsync(
            connectionString, MorningPurchase, At(2026, 11, 20, 10, 0, 0, 123_456),
            (Card, "Debit", 10.50m, "BRL"), (Merchant2, "Credit", 10.50m, "BRL"));
        await AddTransactionAsync(
            connectionString, FirstInstantPurchase, At(2026, 11, 20, 0, 0, 0, 0),
            (Card, "Debit", 1.00m, "BRL"), (Merchant1, "Credit", 1.00m, "BRL"));
        await AddTransactionAsync(
            connectionString, LastInstantPurchase, At(2026, 11, 20, 23, 59, 59, 999_999),
            (Card, "Debit", 2500.00m, "BRL"), (Merchant1, "Credit", 2500.00m, "BRL"));

        await AddTransactionAsync(
            connectionString, PreviousDayPurchase, At(2026, 11, 19, 23, 59, 59, 999_999),
            (Card, "Debit", 5.00m, "BRL"), (Merchant1, "Credit", 5.00m, "BRL"));
        await AddTransactionAsync(
            connectionString, NextDayPurchase, At(2026, 11, 21, 0, 0, 0, 0),
            (Card, "Debit", 6.00m, "BRL"), (Merchant1, "Credit", 6.00m, "BRL"));
        await AddTransactionAsync(
            connectionString, DollarPurchase, At(2026, 11, 20, 9, 0, 0, 0),
            (DollarCard, "Debit", 7.00m, "USD"), (DollarMerchant, "Credit", 7.00m, "USD"));
        await AddTransactionAsync(
            connectionString, CardTopUp, At(2026, 11, 20, 8, 0, 0, 0),
            (SettlementAccount, "Debit", 100.00m, "BRL"), (Card, "Credit", 100.00m, "BRL"));
        await AddTransactionAsync(
            connectionString, MerchantPayout, At(2026, 11, 20, 15, 0, 0, 0),
            (Merchant1, "Debit", 3.00m, "BRL"), (SettlementAccount, "Credit", 3.00m, "BRL"));
    }

    public static Task AddAccountAsync(string connectionString, Guid id, string type, string currency)
    {
        return LedgerDatabase.ExecuteAsync(
            connectionString,
            "INSERT INTO accounts (id, type, currency, created_at, balance) VALUES ($1, $2, $3, $4, 0)",
            id,
            type,
            currency,
            At(2026, 11, 1, 0, 0, 0, 0));
    }

    // Inserts the transaction, then its entries in the given order, so their ids follow that order.
    public static Task AddTransactionAsync(
        string connectionString,
        Guid id,
        DateTimeOffset createdAt,
        params (Guid Account, string Direction, decimal Amount, string Currency)[] entries)
    {
        return AddTransactionAsync(connectionString, id, "Test purchase", createdAt, entries);
    }

    // As above, with the given description; null is stored as NULL, as the Ledger stores a blank description.
    public static async Task AddTransactionAsync(
        string connectionString,
        Guid id,
        string? description,
        DateTimeOffset createdAt,
        params (Guid Account, string Direction, decimal Amount, string Currency)[] entries)
    {
        await LedgerDatabase.ExecuteAsync(
            connectionString,
            "INSERT INTO transactions (id, description, created_at) VALUES ($1, $2, $3)",
            id,
            (object?)description ?? DBNull.Value,
            createdAt);

        foreach ((Guid account, string direction, decimal amount, string currency) in entries)
        {
            await LedgerDatabase.ExecuteAsync(
                connectionString,
                "INSERT INTO entries (transaction_id, account_id, direction, amount, currency) VALUES ($1, $2, $3, $4, $5)",
                id,
                account,
                direction,
                amount,
                currency);
        }
    }

    /// <summary>
    /// Inserts one entry with the given id instead of the next identity value (<c>OVERRIDING SYSTEM VALUE</c>), so that
    /// tests can choose entry ids that do not follow the insertion order. Use ids far above the identity values the
    /// test inserts otherwise.
    /// </summary>
    public static Task AddEntryAsync(
        string connectionString,
        long id,
        Guid transactionId,
        Guid account,
        string direction,
        decimal amount,
        string currency)
    {
        return LedgerDatabase.ExecuteAsync(
            connectionString,
            "INSERT INTO entries (id, transaction_id, account_id, direction, amount, currency) "
                + "OVERRIDING SYSTEM VALUE VALUES ($1, $2, $3, $4, $5, $6)",
            id,
            transactionId,
            account,
            direction,
            amount,
            currency);
    }

    public static DateTimeOffset At(int year, int month, int day, int hour, int minute, int second, int microsecond)
    {
        return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero)
            .AddTicks(microsecond * TimeSpan.TicksPerMicrosecond);
    }

    private static Guid Id(string value)
    {
        return Guid.Parse(value, CultureInfo.InvariantCulture);
    }
}
