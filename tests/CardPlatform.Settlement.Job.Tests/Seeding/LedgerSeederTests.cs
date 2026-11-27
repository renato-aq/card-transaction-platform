using CardPlatform.Settlement.Job.Seeding;
using CardPlatform.Settlement.Job.Tests.Infrastructure;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace CardPlatform.Settlement.Job.Tests.Seeding;

/// <summary>
/// Runs the <see cref="LedgerSeeder"/> in process against a PostgreSQL database with the schema created by the Ledger
/// migrations, and checks what it wrote with plain SQL.
/// </summary>
[Collection(LedgerDatabaseCollectionFixture.Name)]
public sealed class LedgerSeederTests(LedgerDatabaseFixture database)
{
    private static readonly DateOnly Day = new(2026, 11, 20);

    private static readonly DateTimeOffset DayStart = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

    private static readonly SeedOptions SmallSeed = new(
        Transactions: 2_000,
        Merchants: 20,
        Cards: 150,
        Date: Day,
        Currency: "BRL",
        Seed: 7);

    private static readonly string[] LoadedTables = [LedgerSchema.Accounts, LedgerSchema.Transactions, LedgerSchema.Entries];

    [Fact]
    public async Task Seed_writes_exactly_the_requested_number_of_transactions_all_on_the_given_date()
    {
        string connectionString = await database.CreateDatabaseAsync();

        SeedResult result = await SeedAsync(connectionString, SmallSeed);

        Assert.Equal(SmallSeed.Transactions, result.Purchases);
        Assert.Equal(
            SmallSeed.Transactions,
            await LedgerDatabase.ScalarAsync<long>(
                connectionString,
                "SELECT count(*) FROM transactions WHERE created_at >= $1 AND created_at < $2",
                DayStart,
                DayStart.AddDays(1)));
        Assert.Equal(
            SmallSeed.Transactions,
            await LedgerDatabase.ScalarAsync<long>(
                connectionString,
                "SELECT count(*) FROM transactions WHERE description = $1 AND created_at >= $2 AND created_at < $3",
                LedgerSeeder.PurchaseDescription,
                DayStart,
                DayStart.AddDays(1)));

        // The top-ups that fund the cards are dated the day before.
        Assert.Equal(
            result.TopUps,
            await LedgerDatabase.ScalarAsync<long>(
                connectionString,
                "SELECT count(*) FROM transactions WHERE description = $1 AND created_at >= $2 AND created_at < $3",
                LedgerSeeder.TopUpDescription,
                DayStart.AddDays(-1),
                DayStart));
        Assert.Equal(
            SmallSeed.Transactions + result.TopUps,
            await LedgerDatabase.ScalarAsync<long>(connectionString, "SELECT count(*) FROM transactions"));
    }

    [Fact]
    public async Task Seed_keeps_debits_equal_to_credits_and_every_created_account_balance_equal_to_its_entries()
    {
        string connectionString = await database.CreateDatabaseAsync();

        // More cards than purchases, so some cards buy nothing and get no top-up.
        SeedResult result = await SeedAsync(connectionString, SmallSeed with { Cards = 3_000 });

        Assert.Equal(1 + 20 + 3_000, result.Accounts);
        Assert.Equal(
            result.Accounts,
            await LedgerDatabase.ScalarAsync<long>(connectionString, "SELECT count(*) FROM accounts"));

        decimal debits = await LedgerDatabase.ScalarAsync<decimal>(
            connectionString,
            "SELECT sum(amount) FROM entries WHERE direction = 'Debit'");
        decimal credits = await LedgerDatabase.ScalarAsync<decimal>(
            connectionString,
            "SELECT sum(amount) FROM entries WHERE direction = 'Credit'");
        Assert.True(debits > 0);
        Assert.Equal(debits, credits);

        // Every transaction is balanced on its own, in one currency.
        Assert.Equal(
            0L,
            await LedgerDatabase.ScalarAsync<long>(
                connectionString,
                """
                SELECT count(*) FROM (
                    SELECT transaction_id
                    FROM entries
                    GROUP BY transaction_id
                    HAVING sum(CASE direction WHEN 'Credit' THEN amount ELSE -amount END) <> 0
                        OR count(*) < 2
                        OR count(DISTINCT currency) <> 1
                ) AS unbalanced
                """));

        // The stored balance of every account is credits − debits of its entries.
        Assert.Equal(
            0L,
            await LedgerDatabase.ScalarAsync<long>(
                connectionString,
                """
                SELECT count(*)
                FROM accounts AS a
                LEFT JOIN (
                    SELECT account_id, sum(CASE direction WHEN 'Credit' THEN amount ELSE -amount END) AS net
                    FROM entries
                    GROUP BY account_id
                ) AS e ON e.account_id = a.id
                WHERE a.balance <> coalesce(e.net, 0)
                """));

        Assert.Equal(
            0L,
            await LedgerDatabase.ScalarAsync<long>(
                connectionString,
                "SELECT count(*) FROM accounts WHERE type = 'Card' AND balance < 0"));

        // With every transaction balanced, the balances of all accounts add up to zero (ADR 0005).
        Assert.Equal(0m, await LedgerDatabase.ScalarAsync<decimal>(connectionString, "SELECT sum(balance) FROM accounts"));
    }

    [Fact]
    public async Task Seeds_with_the_same_seed_generate_the_same_amounts_per_merchant_and_the_same_instants()
    {
        string first = await database.CreateDatabaseAsync();
        string second = await database.CreateDatabaseAsync();
        string other = await database.CreateDatabaseAsync();

        await SeedAsync(first, SmallSeed);
        await SeedAsync(second, SmallSeed);
        await SeedAsync(other, SmallSeed with { Seed = SmallSeed.Seed + 1 });

        List<(DateTimeOffset OpenedAt, decimal Credited)> firstMerchants = await MerchantTotalsAsync(first);
        Assert.Equal(SmallSeed.Merchants, firstMerchants.Count);
        Assert.Equal(firstMerchants, await MerchantTotalsAsync(second));
        Assert.NotEqual(firstMerchants, await MerchantTotalsAsync(other));

        Assert.Equal(await PurchaseInstantsAsync(first), await PurchaseInstantsAsync(second));
    }

    [Fact]
    public async Task Seed_does_not_change_accounts_that_already_existed()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, SmallSeed);
        List<(Guid Id, decimal Balance, string RowVersion)> before = await AccountsAsync(connectionString);

        SeedResult second = await SeedAsync(connectionString, SmallSeed with { Seed = 99 });

        List<(Guid Id, decimal Balance, string RowVersion)> after = await AccountsAsync(connectionString);
        Assert.Equal(before.Count + second.Accounts, after.Count);
        Assert.All(before, account => Assert.Contains(account, after));
    }

    [Fact]
    public async Task Seed_analyzes_the_tables_it_loaded_so_the_planner_has_their_statistics()
    {
        string connectionString = await database.CreateDatabaseAsync();

        await SeedAsync(connectionString, SmallSeed);

        // last_analyze is set by a manual ANALYZE only (autovacuum sets last_autoanalyze), and the row estimates of the
        // planner match the tables: the tables are small enough for ANALYZE to read every row.
        List<(string Table, DateTimeOffset? LastAnalyze, long EstimatedRows)> statistics = await LedgerDatabase.ListAsync(
            connectionString,
            """
            SELECT s.relname, s.last_analyze, c.reltuples::bigint
            FROM pg_stat_user_tables AS s
            JOIN pg_class AS c ON c.oid = s.relid
            WHERE s.schemaname = current_schema() AND s.relname = ANY($1)
            ORDER BY s.relname
            """,
            static reader => (
                reader.GetString(0),
                reader.IsDBNull(1) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetInt64(2)),
            (object)LoadedTables);

        Assert.Equal(["accounts", "entries", "transactions"], statistics.Select(table => table.Table));
        foreach ((string table, DateTimeOffset? lastAnalyze, long estimatedRows) in statistics)
        {
            Assert.NotNull(lastAnalyze);
            Assert.Equal(
                await LedgerDatabase.ScalarAsync<long>(connectionString, $"SELECT count(*) FROM {table}"),
                estimatedRows);
        }
    }

    [Fact]
    public async Task Seeded_transaction_ids_are_uuid_v7_derived_from_the_transaction_instant()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, SmallSeed with { Transactions = 200 });

        List<(Guid Id, DateTimeOffset CreatedAt)> transactions = await LedgerDatabase.ListAsync(
            connectionString,
            "SELECT id, created_at FROM transactions",
            static reader => (reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1)));

        Assert.NotEmpty(transactions);
        Assert.All(transactions, transaction =>
        {
            Assert.Equal(7, transaction.Id.Version);
            Assert.Equal(transaction.CreatedAt.ToUnixTimeMilliseconds(), UnixMillisecondsOf(transaction.Id));
            Assert.Equal(0, transaction.CreatedAt.Ticks % TimeSpan.TicksPerMicrosecond);
        });
    }

    private static async Task<SeedResult> SeedAsync(string connectionString, SeedOptions options)
    {
        NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using (dataSource.ConfigureAwait(false))
        {
            LedgerSeeder seeder = new(dataSource, NullLogger<LedgerSeeder>.Instance);
            return await seeder.SeedAsync(options, CancellationToken.None);
        }
    }

    // The merchants in the order they were opened, with what the purchases credited to each one.
    private static Task<List<(DateTimeOffset OpenedAt, decimal Credited)>> MerchantTotalsAsync(string connectionString)
    {
        return LedgerDatabase.ListAsync(
            connectionString,
            """
            SELECT a.created_at, sum(e.amount)
            FROM accounts AS a
            JOIN entries AS e ON e.account_id = a.id AND e.direction = 'Credit'
            WHERE a.type = 'Merchant'
            GROUP BY a.id, a.created_at
            ORDER BY a.created_at
            """,
            static reader => (reader.GetFieldValue<DateTimeOffset>(0), reader.GetDecimal(1)));
    }

    private static Task<List<DateTimeOffset>> PurchaseInstantsAsync(string connectionString)
    {
        return LedgerDatabase.ListAsync(
            connectionString,
            "SELECT created_at FROM transactions WHERE description = $1 ORDER BY created_at",
            static reader => reader.GetFieldValue<DateTimeOffset>(0),
            LedgerSeeder.PurchaseDescription);
    }

    private static Task<List<(Guid Id, decimal Balance, string RowVersion)>> AccountsAsync(string connectionString)
    {
        return LedgerDatabase.ListAsync(
            connectionString,
            "SELECT id, balance, xmin::text FROM accounts",
            static reader => (reader.GetGuid(0), reader.GetDecimal(1), reader.GetString(2)));
    }

    // A UUID v7 starts with the 48-bit Unix timestamp in milliseconds, big-endian.
    private static long UnixMillisecondsOf(Guid id)
    {
        byte[] bytes = id.ToByteArray(bigEndian: true);
        long milliseconds = 0;
        for (int index = 0; index < 6; index++)
        {
            milliseconds = (milliseconds << 8) | bytes[index];
        }

        return milliseconds;
    }
}
