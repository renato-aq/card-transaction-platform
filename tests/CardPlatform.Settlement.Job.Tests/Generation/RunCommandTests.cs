using System.Globalization;

using CardPlatform.Settlement.Job.Tests.Infrastructure;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardPlatform.Settlement.Job.Tests.Generation;

/// <summary>
/// Runs the <c>run</c> command through the job's entry point (<see cref="SettlementJobHost"/>, the same code as
/// <c>Program</c>) against a real ledger database, and checks the settlement file it writes.
/// </summary>
[Collection(LedgerDatabaseCollectionFixture.Name)]
public sealed class RunCommandTests(LedgerDatabaseFixture database) : IDisposable
{
    private const int RecordLength = 117;

    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), $"settlement-job-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Run_writes_the_settlement_file_of_a_known_day_line_by_line()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);

        int exitCode = await RunAsync(connectionString, "--date", "2026-11-20", "--currency", "BRL");

        Assert.Equal(ExitCodes.Success, exitCode);
        string[] expected =
        [
            Record("020261120BRL"),
            Detail(1, KnownDay.LastInstantPurchase, KnownDay.Merchant1, "000000000250000", "20261120235959999999"),
            Detail(2, KnownDay.FirstInstantPurchase, KnownDay.Merchant1, "000000000000100", "20261120000000000000"),
            Detail(3, KnownDay.MorningPurchase, KnownDay.Merchant2, "000000000001050", "20261120100000123456"),
            // One transaction crediting the same merchant twice: by entry id, i.e. as inserted (20.00, then 10.00).
            Detail(4, KnownDay.SplitPurchase, KnownDay.Merchant2, "000000000002000", "20261120120000000000"),
            Detail(5, KnownDay.SplitPurchase, KnownDay.Merchant2, "000000000001000", "20261120120000000000"),
            Record("9000000005000000000000254150"),
        ];
        Assert.Equal(expected, ReadLines(SettlementFilePath("20261120", "BRL")));

        // Only the final file is left: the temporary one was renamed.
        Assert.Equal([SettlementFilePath("20261120", "BRL")], Directory.GetFiles(_outputDirectory));
    }

    [Fact]
    public async Task Run_leaves_out_other_days_other_currencies_debits_and_credits_on_accounts_that_are_not_merchants()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);

        int exitCode = await RunAsync(connectionString, "--date=2026-11-20", "--currency=BRL");

        Assert.Equal(ExitCodes.Success, exitCode);
        string[] details = [.. ReadLines(SettlementFilePath("20261120", "BRL")).Where(line => line[0] == '1')];
        HashSet<Guid> transactionIds = [.. details.Select(line => Guid.Parse(line.AsSpan(10, 36), CultureInfo.InvariantCulture))];

        Assert.Equal(
            [KnownDay.LastInstantPurchase, KnownDay.FirstInstantPurchase, KnownDay.MorningPurchase, KnownDay.SplitPurchase],
            transactionIds);
        Assert.DoesNotContain(KnownDay.PreviousDayPurchase, transactionIds);
        Assert.DoesNotContain(KnownDay.NextDayPurchase, transactionIds);
        Assert.DoesNotContain(KnownDay.DollarPurchase, transactionIds);
        Assert.DoesNotContain(KnownDay.CardTopUp, transactionIds);
        Assert.DoesNotContain(KnownDay.MerchantPayout, transactionIds);
    }

    [Fact]
    public async Task Run_settles_a_merchant_credit_whose_transaction_has_no_description()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);
        Guid undescribedPurchase = Guid.Parse("a3000000-0000-0000-0000-000000000003", CultureInfo.InvariantCulture);
        await KnownDay.AddTransactionAsync(
            connectionString,
            undescribedPurchase,
            description: null,
            new DateTimeOffset(2026, 11, 20, 14, 0, 0, TimeSpan.Zero),
            (KnownDay.Card, "Debit", 4.25m, "BRL"),
            (KnownDay.Merchant2, "Credit", 4.25m, "BRL"));
        Assert.Equal(
            1L,
            await LedgerDatabase.ScalarAsync<long>(
                connectionString, "SELECT count(*) FROM transactions WHERE id = $1 AND description IS NULL", undescribedPurchase));

        int exitCode = await RunAsync(connectionString, "--date", "2026-11-20");

        Assert.Equal(ExitCodes.Success, exitCode);
        string[] lines = ReadLines(SettlementFilePath("20261120", "BRL"));
        Assert.Contains(
            Detail(4, undescribedPurchase, KnownDay.Merchant2, "000000000000425", "20261120140000000000"),
            lines);
        Assert.Equal(Record("9000000006000000000000254575"), lines[^1]);
    }

    [Fact]
    public async Task Run_options_given_without_the_run_word_write_the_file_of_the_given_day_and_currency()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);

        int exitCode = await SettlementJobHost.RunAsync(
            ["--date", "2026-11-20", "--currency=USD", "--output", _outputDirectory],
            builder => UseLedgerDatabase(builder, connectionString));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal([SettlementFilePath("20261120", "USD")], Directory.GetFiles(_outputDirectory));
        string[] lines = ReadLines(SettlementFilePath("20261120", "USD"));
        Assert.Equal(
            [
                Record("020261120USD"),
                Detail(1, KnownDay.DollarPurchase, KnownDay.DollarMerchant, "000000000000700", "20261120090000000000"),
                Record("9000000001000000000000000700"),
            ],
            lines);
    }

    [Fact]
    public async Task Run_on_a_day_without_records_writes_the_header_and_a_zero_trailer()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);

        int exitCode = await RunAsync(connectionString, "--date", "2026-11-22");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            [Record("020261122BRL"), Record("9000000000000000000000000000")],
            ReadLines(SettlementFilePath("20261122", "BRL")));
    }

    [Fact]
    public async Task Run_trailer_count_and_sum_match_a_direct_query_of_the_database()
    {
        string connectionString = await database.CreateDatabaseAsync();
        int seedExitCode = await SettlementJobHost.RunAsync(
            ["seed", "--transactions", "3000", "--merchants", "7", "--cards", "40", "--date", "2026-11-20", "--seed", "11"],
            builder => UseLedgerDatabase(builder, connectionString));
        Assert.Equal(ExitCodes.Success, seedExitCode);
        await KnownDay.ArrangeAsync(connectionString);

        int exitCode = await RunAsync(connectionString, "--date", "2026-11-20");

        Assert.Equal(ExitCodes.Success, exitCode);
        string[] lines = ReadLines(SettlementFilePath("20261120", "BRL"));
        string trailer = lines[^1];
        long trailerCount = long.Parse(trailer.AsSpan(1, 9), CultureInfo.InvariantCulture);
        long trailerTotalCents = long.Parse(trailer.AsSpan(10, 18), CultureInfo.InvariantCulture);

        const string Settled =
            """
            FROM entries e
            JOIN accounts a ON a.id = e.account_id
            JOIN transactions t ON t.id = e.transaction_id
            WHERE e.direction = 'Credit' AND a.type = 'Merchant' AND e.currency = 'BRL'
              AND t.created_at >= '2026-11-20T00:00:00Z' AND t.created_at < '2026-11-21T00:00:00Z'
            """;
        long count = await LedgerDatabase.ScalarAsync<long>(connectionString, $"SELECT count(*) {Settled}");
        decimal total = await LedgerDatabase.ScalarAsync<decimal>(connectionString, $"SELECT coalesce(sum(e.amount), 0) {Settled}");

        Assert.Equal(3005L, count); // the seeded purchases and the 5 credits of the known day
        Assert.Equal(count, trailerCount);
        Assert.Equal(total * 100m, trailerTotalCents);
        Assert.Equal(count, lines.Length - 2);
        Assert.Equal(trailerTotalCents, lines[1..^1].Sum(line => long.Parse(line.AsSpan(82, 15), CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task A_failed_run_exits_with_a_failure_and_leaves_no_settlement_file()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);

        // 10^13 reais is 10^15 cents, 16 digits: more than the amount field holds. The merchant sorts last, so the file
        // already has lines when the formatter throws.
        Guid lastMerchant = Guid.Parse("ffffffff-0000-0000-0000-000000000001", CultureInfo.InvariantCulture);
        await KnownDay.AddAccountAsync(connectionString, lastMerchant, "Merchant", "BRL");
        await KnownDay.AddTransactionAsync(
            connectionString,
            Guid.Parse("b0000000-0000-0000-0000-000000000001", CultureInfo.InvariantCulture),
            new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero),
            (KnownDay.Card, "Debit", 10_000_000_000_000m, "BRL"),
            (lastMerchant, "Credit", 10_000_000_000_000m, "BRL"));
        using CapturedLogs logs = new();

        int exitCode = await RunAsync(connectionString, logs, "--date", "2026-11-20", "--output", _outputDirectory);

        Assert.Equal(ExitCodes.Failure, exitCode);
        CapturedLogEntry error = Assert.Single(logs.Errors);
        Assert.Contains("no settlement file was written", error.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentOutOfRangeException>(error.Exception);

        // Neither the final file nor the temporary one is left.
        Assert.Empty(Directory.GetFiles(_outputDirectory));
    }

    [Fact]
    public async Task Run_never_writes_to_the_ledger_tables()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);

        // Every row of the three tables, with its xmin, which changes on any update even to the same values.
        const string Fingerprint =
            """
            SELECT md5(
                coalesce((SELECT string_agg(a::text || a.xmin::text, ';' ORDER BY a.id) FROM accounts a), '')
                || coalesce((SELECT string_agg(t::text || t.xmin::text, ';' ORDER BY t.id) FROM transactions t), '')
                || coalesce((SELECT string_agg(e::text || e.xmin::text, ';' ORDER BY e.id) FROM entries e), ''))
            """;
        string before = await LedgerDatabase.ScalarAsync<string>(connectionString, Fingerprint);

        int exitCode = await RunAsync(connectionString, "--date", "2026-11-20");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(before, await LedgerDatabase.ScalarAsync<string>(connectionString, Fingerprint));
    }

    [Fact]
    public async Task Run_exits_with_a_failure_when_the_ledger_connection_string_is_missing()
    {
        using CapturedLogs logs = new();

        int exitCode = await SettlementJobHost.RunAsync(
            ["run", "--output", _outputDirectory],
            builder =>
            {
                // Blank out any connection string the environment could provide.
                builder.Configuration[$"ConnectionStrings:{SettlementJobHost.LedgerConnectionName}"] = string.Empty;
                builder.Configuration["Aspire:Npgsql:EntityFrameworkCore:PostgreSQL:ConnectionString"] = string.Empty;
                builder.Logging.AddProvider(logs);
            });

        Assert.Equal(ExitCodes.Failure, exitCode);
        CapturedLogEntry error = Assert.Single(logs.Errors);
        Assert.Contains("connection string 'ledgerdb' is missing", error.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__ledgerdb", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_outputDirectory));
    }

    private Task<int> RunAsync(string connectionString, params string[] options)
    {
        return SettlementJobHost.RunAsync(
            ["run", .. options, "--output", _outputDirectory],
            builder => UseLedgerDatabase(builder, connectionString));
    }

    private static Task<int> RunAsync(string connectionString, CapturedLogs logs, params string[] options)
    {
        return SettlementJobHost.RunAsync(
            ["run", .. options],
            builder =>
            {
                UseLedgerDatabase(builder, connectionString);
                builder.Logging.AddProvider(logs);
            });
    }

    private static void UseLedgerDatabase(HostApplicationBuilder builder, string connectionString)
    {
        builder.Configuration[$"ConnectionStrings:{SettlementJobHost.LedgerConnectionName}"] = connectionString;
    }

    private string SettlementFilePath(string date, string currency)
    {
        return Path.Combine(_outputDirectory, $"settlement-{date}-{currency}.txt");
    }

    // The lines of the file, each checked to end with the line terminator.
    private static string[] ReadLines(string path)
    {
        string content = File.ReadAllText(path);
        Assert.EndsWith("\n", content, StringComparison.Ordinal);
        return content[..^1].Split('\n');
    }

    private static string Record(string fields)
    {
        return fields.PadRight(RecordLength);
    }

    private static string Detail(int sequence, Guid transactionId, Guid merchantId, string amount, string instant)
    {
        return Record(string.Create(
            CultureInfo.InvariantCulture,
            $"1{sequence:D9}{transactionId:D}{merchantId:D}{amount}{instant}"));
    }

    /// <summary>
    /// A known set of ledger rows around 2026-11-20 (UTC): five merchant credits in BRL within the day, including both
    /// ends of the day, and rows that must be left out of its file.
    /// </summary>
    private static class KnownDay
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

        private static DateTimeOffset At(int year, int month, int day, int hour, int minute, int second, int microsecond)
        {
            return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero)
                .AddTicks(microsecond * TimeSpan.TicksPerMicrosecond);
        }

        private static Guid Id(string value)
        {
            return Guid.Parse(value, CultureInfo.InvariantCulture);
        }
    }
}
