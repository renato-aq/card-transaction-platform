using System.Globalization;
using System.Text.Json;

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

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public async Task Run_writes_the_settlement_file_of_a_known_day_line_by_line(string strategy)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);

        int exitCode = await RunAsync(connectionString, "--strategy", strategy, "--date", "2026-11-20", "--currency", "BRL");

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

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public async Task Run_leaves_out_other_days_other_currencies_debits_and_credits_on_accounts_that_are_not_merchants(string strategy)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);

        int exitCode = await RunAsync(connectionString, "--strategy", strategy, "--date=2026-11-20", "--currency=BRL");

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

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public async Task Run_settles_a_merchant_credit_whose_transaction_has_no_description(string strategy)
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

        int exitCode = await RunAsync(connectionString, "--strategy", strategy, "--date", "2026-11-20");

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

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public async Task Run_on_a_day_without_records_writes_the_header_and_a_zero_trailer(string strategy)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);

        int exitCode = await RunAsync(connectionString, "--strategy", strategy, "--date", "2026-11-22");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            [Record("020261122BRL"), Record("9000000000000000000000000000")],
            ReadLines(SettlementFilePath("20261122", "BRL")));
    }

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public async Task Run_trailer_count_and_sum_match_a_direct_query_of_the_database(string strategy)
    {
        string connectionString = await database.CreateDatabaseAsync();
        int seedExitCode = await SettlementJobHost.RunAsync(
            ["seed", "--transactions", "3000", "--merchants", "7", "--cards", "40", "--date", "2026-11-20", "--seed", "11"],
            builder => UseLedgerDatabase(builder, connectionString));
        Assert.Equal(ExitCodes.Success, seedExitCode);
        await KnownDay.ArrangeAsync(connectionString);

        int exitCode = await RunAsync(connectionString, "--strategy", strategy, "--date", "2026-11-20");

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

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public async Task A_failed_run_exits_with_a_failure_and_leaves_no_settlement_file(string strategy)
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

        int exitCode = await RunAsync(connectionString, logs, "--strategy", strategy, "--date", "2026-11-20", "--output", _outputDirectory);

        Assert.Equal(ExitCodes.Failure, exitCode);
        CapturedLogEntry error = Assert.Single(logs.Errors);
        Assert.Contains("no settlement file was written", error.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentOutOfRangeException>(error.Exception);

        // Neither the final file nor the temporary one is left.
        Assert.Empty(Directory.GetFiles(_outputDirectory));
    }

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public async Task Run_never_writes_to_the_ledger_tables(string strategy)
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

        int exitCode = await RunAsync(connectionString, "--strategy", strategy, "--date", "2026-11-20");

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

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public async Task Run_with_metrics_output_writes_a_json_with_every_field_and_the_count_of_the_trailer(string strategy)
    {
        string connectionString = await database.CreateDatabaseAsync();
        int seedExitCode = await SettlementJobHost.RunAsync(
            ["seed", "--transactions", "2000", "--merchants", "5", "--cards", "30", "--date", "2026-11-20", "--seed", "7"],
            builder => UseLedgerDatabase(builder, connectionString));
        Assert.Equal(ExitCodes.Success, seedExitCode);
        string metricsPath = Path.Combine(_outputDirectory, "metrics", "run.json");

        int exitCode = await RunAsync(connectionString, "--strategy", strategy, "--date", "2026-11-20", "--metrics-output", metricsPath);

        Assert.Equal(ExitCodes.Success, exitCode);
        string settlementFile = SettlementFilePath("20261120", "BRL");
        long trailerCount = long.Parse(ReadLines(settlementFile)[^1].AsSpan(1, 9), CultureInfo.InvariantCulture);

        using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(metricsPath));
        JsonElement metrics = json.RootElement;
        Assert.Equal(
            [
                "strategy", "date", "currency", "recordCount", "fileSizeBytes", "elapsedMilliseconds", "cpuTimeMilliseconds",
                "peakWorkingSetBytes", "allocatedBytes", "parallelism",
            ],
            metrics.EnumerateObject().Select(property => property.Name));
        Assert.Equal(strategy, metrics.GetProperty("strategy").GetString());
        Assert.Equal("2026-11-20", metrics.GetProperty("date").GetString());
        Assert.Equal("BRL", metrics.GetProperty("currency").GetString());
        Assert.Equal(2000L, trailerCount);
        Assert.Equal(trailerCount, metrics.GetProperty("recordCount").GetInt64());
        Assert.Equal(new FileInfo(settlementFile).Length, metrics.GetProperty("fileSizeBytes").GetInt64());
        Assert.True(metrics.GetProperty("elapsedMilliseconds").GetDouble() > 0);
        Assert.True(metrics.GetProperty("cpuTimeMilliseconds").GetDouble() > 0);
        Assert.True(metrics.GetProperty("peakWorkingSetBytes").GetInt64() > 0);
        Assert.True(metrics.GetProperty("allocatedBytes").GetInt64() > 0);

        // Only the final metrics file is left: the temporary one was renamed.
        Assert.Equal([metricsPath], Directory.GetFiles(Path.GetDirectoryName(metricsPath)!));
    }

    [Fact]
    public async Task Run_writes_the_same_settlement_file_with_and_without_metrics_output()
    {
        string connectionString = await database.CreateDatabaseAsync();
        int seedExitCode = await SettlementJobHost.RunAsync(
            ["seed", "--transactions", "1000", "--merchants", "4", "--cards", "20", "--date", "2026-11-20", "--seed", "3"],
            builder => UseLedgerDatabase(builder, connectionString));
        Assert.Equal(ExitCodes.Success, seedExitCode);
        await KnownDay.ArrangeAsync(connectionString);

        int withoutMetricsExitCode = await RunAsync(connectionString, "--date", "2026-11-20");
        byte[] withoutMetrics = await File.ReadAllBytesAsync(SettlementFilePath("20261120", "BRL"));
        int withMetricsExitCode = await RunAsync(
            connectionString, "--date", "2026-11-20", "--metrics-output", Path.Combine(_outputDirectory, "metrics", "run.json"));
        byte[] withMetrics = await File.ReadAllBytesAsync(SettlementFilePath("20261120", "BRL"));

        Assert.Equal(ExitCodes.Success, withoutMetricsExitCode);
        Assert.Equal(ExitCodes.Success, withMetricsExitCode);
        Assert.Equal(1005 * (RecordLength + 1) + (2 * (RecordLength + 1)), withoutMetrics.Length);
        Assert.Equal(withoutMetrics, withMetrics);
    }

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public async Task Every_successful_run_logs_its_summary_and_writes_no_metrics_file_unless_asked(string strategy)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);
        using CapturedLogs logs = new();

        int exitCode = await RunAsync(connectionString, logs, "--strategy", strategy, "--date", "2026-11-20", "--output", _outputDirectory);

        Assert.Equal(ExitCodes.Success, exitCode);
        CapturedLogEntry summary = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Run summary:", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, summary.Level);
        long fileSize = new FileInfo(SettlementFilePath("20261120", "BRL")).Length;
        Assert.StartsWith(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Run summary: strategy {strategy}, date 2026-11-20, currency BRL, 5 records, file {fileSize} bytes, elapsed "),
            summary.Message,
            StringComparison.Ordinal);
        Assert.Contains(" ms, CPU ", summary.Message, StringComparison.Ordinal);
        Assert.Contains(" ms, peak working set ", summary.Message, StringComparison.Ordinal);
        Assert.Contains(" bytes, allocated ", summary.Message, StringComparison.Ordinal);
        Assert.Equal([SettlementFilePath("20261120", "BRL")], Directory.GetFiles(_outputDirectory, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("naive")]
    [InlineData("optimized")]
    public async Task A_failed_run_writes_no_metrics_file_and_removes_the_one_of_an_earlier_run(string strategy)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);
        string metricsDirectory = Path.Combine(_outputDirectory, "metrics");
        string metricsPath = Path.Combine(metricsDirectory, "run.json");
        Assert.Equal(ExitCodes.Success, await RunAsync(connectionString, "--strategy", strategy, "--date", "2026-11-20", "--metrics-output", metricsPath));
        Assert.True(File.Exists(metricsPath));

        // The same amount out of range as in the failed run above: the formatter throws mid-file.
        Guid lastMerchant = Guid.Parse("ffffffff-0000-0000-0000-000000000001", CultureInfo.InvariantCulture);
        await KnownDay.AddAccountAsync(connectionString, lastMerchant, "Merchant", "BRL");
        await KnownDay.AddTransactionAsync(
            connectionString,
            Guid.Parse("b0000000-0000-0000-0000-000000000001", CultureInfo.InvariantCulture),
            new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero),
            (KnownDay.Card, "Debit", 10_000_000_000_000m, "BRL"),
            (lastMerchant, "Credit", 10_000_000_000_000m, "BRL"));
        using CapturedLogs logs = new();

        int exitCode = await RunAsync(
            connectionString,
            logs,
            "--strategy",
            strategy,
            "--date",
            "2026-11-20",
            "--output",
            _outputDirectory,
            "--metrics-output",
            metricsPath);

        Assert.Equal(ExitCodes.Failure, exitCode);
        Assert.Single(logs.Errors);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.StartsWith("Run summary:", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(metricsDirectory));
    }

    [Theory]
    [InlineData("literal")]
    [InlineData("relative segments")]
    [InlineData("output with a trailing separator")]
    public async Task A_metrics_output_that_is_the_settlement_file_is_rejected_before_the_run_and_leaves_the_file_untouched(string form)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);
        string settlementFile = SettlementFilePath("20261120", "BRL");
        Directory.CreateDirectory(_outputDirectory);
        byte[] existing = "a settlement file of an earlier run\n"u8.ToArray();
        await File.WriteAllBytesAsync(settlementFile, existing);
        (string outputDirectory, string metricsOutput) = form switch
        {
            "literal" => (_outputDirectory, settlementFile),
            "relative segments" => (_outputDirectory, Path.Combine(_outputDirectory, "metrics", "..", ".", "settlement-20261120-BRL.txt")),
            _ => (_outputDirectory + Path.DirectorySeparatorChar, settlementFile),
        };
        using CapturedLogs logs = new();

        (int exitCode, string standardError) = await CaptureStandardErrorAsync(() => RunAsync(
            connectionString,
            logs,
            "--date",
            "2026-11-20",
            "--output",
            outputDirectory,
            "--metrics-output",
            metricsOutput));

        Assert.Equal(ExitCodes.InvalidArguments, exitCode);
        Assert.Contains(
            $"Invalid value '{metricsOutput}' for option '--metrics-output': {settlementFile} is the settlement file of this run",
            standardError,
            StringComparison.Ordinal);
        Assert.Contains("Usage:", standardError, StringComparison.Ordinal);

        // Nothing ran: no log, the earlier file is intact, and no metrics file or temporary file was created.
        Assert.Empty(logs.Entries);
        Assert.Equal(existing, await File.ReadAllBytesAsync(settlementFile));
        Assert.Equal([settlementFile], Directory.GetFiles(_outputDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_metrics_output_in_the_directory_of_the_settlement_file_writes_both_files()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);
        string metricsPath = Path.Combine(_outputDirectory, "settlement-20261120-BRL.json");

        int exitCode = await RunAsync(connectionString, "--date", "2026-11-20", "--metrics-output", metricsPath);

        Assert.Equal(ExitCodes.Success, exitCode);
        string settlementFile = SettlementFilePath("20261120", "BRL");
        Assert.Equal(Record("9000000005000000000000254150"), ReadLines(settlementFile)[^1]);
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(metricsPath));
        Assert.Equal(5L, json.RootElement.GetProperty("recordCount").GetInt64());
        Assert.Equal(
            [metricsPath, settlementFile],
            Directory.GetFiles(_outputDirectory).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Run_without_a_strategy_uses_the_optimized_one()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);
        using CapturedLogs logs = new();

        int exitCode = await RunAsync(connectionString, logs, "--date", "2026-11-20", "--output", _outputDirectory);

        Assert.Equal(ExitCodes.Success, exitCode);
        CapturedLogEntry summary = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Run summary:", StringComparison.Ordinal));
        Assert.StartsWith("Run summary: strategy optimized, ", summary.Message, StringComparison.Ordinal);
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

    // The job writes an invalid command line to the standard error. Other test classes may write to it at the same
    // time, so the capture is synchronized and only checked with Contains.
    private static async Task<(int ExitCode, string StandardError)> CaptureStandardErrorAsync(Func<Task<int>> run)
    {
        TextWriter original = Console.Error;
        using StringWriter captured = new(CultureInfo.InvariantCulture);
        Console.SetError(TextWriter.Synchronized(captured));
        try
        {
            int exitCode = await run();
            return (exitCode, captured.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
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
}
