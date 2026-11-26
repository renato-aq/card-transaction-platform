using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;

using CardPlatform.Settlement.Job.Generation;
using CardPlatform.Settlement.Job.ReadModel;
using CardPlatform.Settlement.Job.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace CardPlatform.Settlement.Job.Tests.Generation;

/// <summary>
/// The <c>optimized</c> strategy split into partitions by merchant and read in parallel (<c>--parallelism</c>) must write
/// exactly the file of the <c>naive</c> one, whatever the degree, with the details numbered without a gap, and a partition
/// that fails must fail the run without leaving any file behind. Runs go through the job's entry point against a real
/// ledger database, with page sizes small enough that pages end inside a merchant.
/// </summary>
[Collection(LedgerDatabaseCollectionFixture.Name)]
public sealed class ParallelismTests(LedgerDatabaseFixture database) : IDisposable
{
    private const string Day = "2026-11-20";

    private const int PageSize = 3;

    // Sorts before every seeded merchant (UUID v7 of the day before) and every merchant of the known day.
    private static readonly Guid FirstMerchant = Id("00000000-0000-0000-0000-000000000001");

    // Sorts after every other merchant.
    private static readonly Guid LastMerchant = Id("ffffffff-0000-0000-0000-000000000001");

    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), $"settlement-parallelism-tests-{Guid.NewGuid():N}");

    /// <summary>The days the byte comparison is made on.</summary>
    public enum Scenario
    {
        /// <summary>Ten merchants (seven seeded and three of the known day, one in USD) with their records spread.</summary>
        SeveralMerchants,

        /// <summary>Three merchants: fewer than the partitions of a parallelism of 8.</summary>
        FewerMerchantsThanPartitions,

        /// <summary>A single merchant holding every record.</summary>
        SingleMerchant,

        /// <summary>One seeded merchant holding 400 of the 408 records, between six merchants with few or none.</summary>
        OneMerchantHoldingMostRecords,

        /// <summary>Merchants, but no record on the day.</summary>
        DayWithoutRecords,
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(Scenario.SeveralMerchants, 1)]
    [InlineData(Scenario.SeveralMerchants, 2)]
    [InlineData(Scenario.SeveralMerchants, 8)]
    [InlineData(Scenario.FewerMerchantsThanPartitions, 1)]
    [InlineData(Scenario.FewerMerchantsThanPartitions, 2)]
    [InlineData(Scenario.FewerMerchantsThanPartitions, 8)]
    [InlineData(Scenario.SingleMerchant, 1)]
    [InlineData(Scenario.SingleMerchant, 2)]
    [InlineData(Scenario.SingleMerchant, 8)]
    [InlineData(Scenario.OneMerchantHoldingMostRecords, 1)]
    [InlineData(Scenario.OneMerchantHoldingMostRecords, 2)]
    [InlineData(Scenario.OneMerchantHoldingMostRecords, 8)]
    [InlineData(Scenario.DayWithoutRecords, 1)]
    [InlineData(Scenario.DayWithoutRecords, 2)]
    [InlineData(Scenario.DayWithoutRecords, 8)]
    public async Task The_optimized_strategy_writes_the_bytes_of_the_naive_one_at_every_parallelism(Scenario scenario, int parallelism)
    {
        string connectionString = await database.CreateDatabaseAsync();
        (int expectedRecords, int expectedMerchants) = await ArrangeAsync(connectionString, scenario);

        byte[] naive = await GenerateAsync(connectionString, "naive", parallelism: 1);
        byte[] optimized = await GenerateAsync(connectionString, "optimized", parallelism);

        Assert.Equal(naive, optimized);
        string[] details = Details(optimized);
        Assert.Equal(expectedRecords, details.Length);
        Assert.Equal(expectedMerchants, details.Select(line => line[46..82]).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public async Task The_details_of_a_parallel_file_are_numbered_from_1_to_the_count_of_the_trailer(int parallelism)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, transactions: 500, merchants: 11, seed: 21);
        await KnownDay.ArrangeAsync(connectionString);

        byte[] file = await GenerateAsync(connectionString, "optimized", parallelism);

        string[] lines = Lines(file);
        long trailerCount = long.Parse(lines[^1].AsSpan(1, 9), CultureInfo.InvariantCulture);
        Assert.Equal(505L, trailerCount);
        Assert.Equal(
            Enumerable.Range(1, (int)trailerCount).Select(number => number.ToString("D9", CultureInfo.InvariantCulture)),
            Details(file).Select(line => line[1..10]));
    }

    // The merchant whose amount does not fit the detail field sorts first or last, so the failing partition is the first
    // one, while later partitions are still being read into their intermediate files, or the last one, after earlier
    // partitions were appended to the settlement file.
    [Theory]
    [InlineData(true, 2)]
    [InlineData(true, 8)]
    [InlineData(false, 2)]
    [InlineData(false, 8)]
    public async Task A_failing_partition_fails_the_run_and_leaves_no_settlement_file_and_no_intermediate_file(
        bool failingMerchantSortsFirst,
        int parallelism)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, transactions: 600, merchants: 9, seed: 17);
        await KnownDay.ArrangeAsync(connectionString);
        Guid failingMerchant = failingMerchantSortsFirst ? FirstMerchant : LastMerchant;
        await KnownDay.AddAccountAsync(connectionString, failingMerchant, "Merchant", "BRL");

        // 10^13 reais is 10^15 cents, 16 digits: more than the amount field holds.
        await KnownDay.AddTransactionAsync(
            connectionString,
            Id("b0000000-0000-0000-0000-000000000001"),
            KnownDay.At(2026, 11, 20, 18, 0, 0, 0),
            (KnownDay.Card, "Debit", 10_000_000_000_000m, "BRL"),
            (failingMerchant, "Credit", 10_000_000_000_000m, "BRL"));
        using CapturedLogs logs = new();

        int exitCode = await SettlementJobHost.RunAsync(
            [
                "run", "--strategy", "optimized", "--parallelism", parallelism.ToString(CultureInfo.InvariantCulture),
                "--page-size", PageSize.ToString(CultureInfo.InvariantCulture), "--date", Day, "--output", _outputDirectory,
            ],
            builder =>
            {
                UseLedgerDatabase(builder, connectionString);
                builder.Logging.AddProvider(logs);
            });

        Assert.NotEqual(ExitCodes.Success, exitCode);
        Assert.Equal(ExitCodes.Failure, exitCode);

        // The error of the failing partition, not the cancellation it caused in the others.
        CapturedLogEntry error = Assert.Single(logs.Errors);
        Assert.Contains("no settlement file was written", error.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentOutOfRangeException>(error.Exception);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.StartsWith("Run summary:", StringComparison.Ordinal));

        // Neither the final file, nor its temporary file, nor any intermediate file of a partition is left.
        Assert.True(Directory.Exists(_outputDirectory));
        Assert.Empty(Directory.GetFileSystemEntries(_outputDirectory, "*", SearchOption.AllDirectories));
    }

    // The queries of the other merchant are held until they are cancelled, or released after a limit no passing run
    // reaches. The failing merchant sorts first, so the partition the join awaits fails while the other is held, or last,
    // so the join awaits the held partition while a later one fails: only that partition can cancel it.
    [Theory]
    [InlineData(true, 2)]
    [InlineData(true, 8)]
    [InlineData(false, 2)]
    [InlineData(false, 8)]
    public async Task A_failing_partition_cancels_the_reading_of_the_other_partitions(bool failingMerchantSortsFirst, int parallelism)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);
        Guid failingMerchant = failingMerchantSortsFirst ? FirstMerchant : LastMerchant;
        Guid heldMerchant = failingMerchantSortsFirst ? LastMerchant : FirstMerchant;
        await KnownDay.AddAccountAsync(connectionString, FirstMerchant, "Merchant", "BRL");
        await KnownDay.AddAccountAsync(connectionString, LastMerchant, "Merchant", "BRL");
        await KnownDay.AddTransactionAsync(
            connectionString,
            Id("b0000000-0000-0000-0000-000000000001"),
            KnownDay.At(2026, 11, 20, 18, 0, 0, 0),
            (KnownDay.Card, "Debit", 10_000_000_000_000m, "BRL"),
            (failingMerchant, "Credit", 10_000_000_000_000m, "BRL"));
        await KnownDay.AddTransactionAsync(
            connectionString,
            Id("b0000000-0000-0000-0000-000000000002"),
            KnownDay.At(2026, 11, 20, 18, 0, 1, 0),
            (KnownDay.Card, "Debit", 1m, "BRL"),
            (heldMerchant, "Credit", 1m, "BRL"));
        HeldQueries held = new(heldMerchant, TimeSpan.FromSeconds(30));
        using CapturedLogs logs = new();

        int exitCode = await SettlementJobHost.RunAsync(
            [
                "run", "--strategy", "optimized", "--parallelism", parallelism.ToString(CultureInfo.InvariantCulture),
                "--date", Day, "--output", _outputDirectory,
            ],
            builder =>
            {
                UseLedgerDatabase(builder, connectionString);
                builder.Logging.AddProvider(logs);
                builder.Services.ConfigureDbContext<LedgerReadDbContext>(options => options.AddInterceptors(held));
            });

        Assert.Equal(ExitCodes.Failure, exitCode);
        Assert.IsType<ArgumentOutOfRangeException>(Assert.Single(logs.Errors).Exception);

        // Every held query was cancelled, none ran out its limit.
        Assert.Equal(0, held.Released);
        Assert.Empty(Directory.GetFileSystemEntries(_outputDirectory, "*", SearchOption.AllDirectories));
    }

    // The run is held after it exported its snapshot and before it read the merchants (the accounts table is locked),
    // while a credit to a merchant of the last partition is committed. The partitions start reading only after that, so
    // a partition that took a snapshot of its own would see the credit; importing the exported one, none does.
    [Fact]
    public async Task Every_partition_reads_the_snapshot_exported_before_the_merchants_were_read()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, transactions: 200, merchants: 6, seed: 12);
        await KnownDay.ArrangeAsync(connectionString);
        byte[] before = await GenerateAsync(connectionString, "naive", parallelism: 1);
        Task<byte[]> run;

        NpgsqlConnection blocker = new(connectionString);
        await using (blocker)
        {
            await blocker.OpenAsync();
            NpgsqlTransaction transaction = await blocker.BeginTransactionAsync();
            await using (transaction)
            {
                await ExecuteAsync(blocker, "LOCK TABLE accounts IN ACCESS EXCLUSIVE MODE");
                Guid latePurchase = Id("b1000000-0000-0000-0000-000000000001");
                await ExecuteAsync(
                    blocker,
                    "INSERT INTO transactions (id, description, created_at) VALUES ($1, 'Late purchase', $2)",
                    latePurchase,
                    KnownDay.At(2026, 11, 20, 16, 0, 0, 0));
                await ExecuteAsync(
                    blocker,
                    "INSERT INTO entries (transaction_id, account_id, direction, amount, currency) VALUES "
                        + "($1, $2, 'Debit', 9.99, 'BRL'), ($1, $3, 'Credit', 9.99, 'BRL')",
                    latePurchase,
                    KnownDay.Card,
                    KnownDay.Merchant2);

                // Nine merchants in four partitions: the second merchant of the known day is in the last one.
                run = Task.Run(() => GenerateAsync(connectionString, "optimized", parallelism: 4));
                await WaitForARunWaitingOnALockAsync(connectionString);
                await transaction.CommitAsync();
            }
        }

        byte[] parallel = await run;
        byte[] after = await GenerateAsync(connectionString, "naive", parallelism: 1);

        Assert.Equal(before, parallel);
        Assert.Equal(Details(before).Length + 1, Details(after).Length);
    }

    [Fact]
    public async Task A_successful_parallel_run_leaves_only_the_settlement_file()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, transactions: 200, merchants: 6, seed: 4);

        await GenerateAsync(connectionString, "optimized", parallelism: 4);

        Assert.Equal(
            [Path.Combine(_outputDirectory, "optimized-4", "settlement-20261120-BRL.txt")],
            Directory.GetFileSystemEntries(Path.Combine(_outputDirectory, "optimized-4"), "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("optimized", "3", 3)]
    [InlineData("optimized", "1", 1)]
    [InlineData("naive", "3", 1)]
    public async Task The_metrics_and_the_summary_report_the_parallelism_of_the_run(string strategy, string parallelism, int reported)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);
        string metricsPath = Path.Combine(_outputDirectory, "metrics", "run.json");
        using CapturedLogs logs = new();

        int exitCode = await SettlementJobHost.RunAsync(
            [
                "run", "--strategy", strategy, "--parallelism", parallelism, "--date", Day, "--output", _outputDirectory,
                "--metrics-output", metricsPath,
            ],
            builder =>
            {
                UseLedgerDatabase(builder, connectionString);
                builder.Logging.AddProvider(logs);
            });

        Assert.Equal(ExitCodes.Success, exitCode);
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(metricsPath));
        Assert.Equal(reported, json.RootElement.GetProperty("parallelism").GetInt32());
        Assert.Equal(5L, json.RootElement.GetProperty("recordCount").GetInt64());
        CapturedLogEntry summary = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Run summary:", StringComparison.Ordinal));
        Assert.EndsWith(
            string.Create(CultureInfo.InvariantCulture, $" bytes, parallelism {reported}"),
            summary.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_without_parallelism_uses_the_default_one()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await KnownDay.ArrangeAsync(connectionString);
        string metricsPath = Path.Combine(_outputDirectory, "metrics", "run.json");

        int exitCode = await SettlementJobHost.RunAsync(
            ["run", "--date", Day, "--output", _outputDirectory, "--metrics-output", metricsPath],
            builder => UseLedgerDatabase(builder, connectionString));

        Assert.Equal(ExitCodes.Success, exitCode);
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(metricsPath));
        Assert.Equal(RunOptions.DefaultParallelism, json.RootElement.GetProperty("parallelism").GetInt32());
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params object[] parameters)
    {
        NpgsqlCommand command = new(sql, connection);
        await using (command)
        {
            foreach (object parameter in parameters)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = parameter });
            }

            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task WaitForARunWaitingOnALockAsync(string connectionString)
    {
        const string Waiting =
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'";
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        while (await LedgerDatabase.ScalarAsync<long>(connectionString, Waiting) == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    // Arranges the day of the scenario and returns how many records and merchants its file must have.
    private static async Task<(int Records, int Merchants)> ArrangeAsync(string connectionString, Scenario scenario)
    {
        switch (scenario)
        {
            case Scenario.SeveralMerchants:
                await SeedAsync(connectionString, transactions: 300, merchants: 7, seed: 5);
                await KnownDay.ArrangeAsync(connectionString);
                return (305, 9);

            case Scenario.FewerMerchantsThanPartitions:
                await SeedAsync(connectionString, transactions: 60, merchants: 3, seed: 3);
                return (60, 3);

            case Scenario.SingleMerchant:
                await SeedAsync(connectionString, transactions: 50, merchants: 1, seed: 1);
                return (50, 1);

            case Scenario.OneMerchantHoldingMostRecords:
                // The seeded merchant holds 400 records; it sorts after the three small merchants and before the merchants
                // of the known day (two with five records, one in USD with none in BRL).
                await SeedAsync(connectionString, transactions: 400, merchants: 1, seed: 8);
                await KnownDay.ArrangeAsync(connectionString);
                for (int merchant = 1; merchant <= 3; merchant++)
                {
                    Guid id = Id(string.Create(CultureInfo.InvariantCulture, $"00000000-0000-0000-0000-00000000000{merchant}"));
                    await KnownDay.AddAccountAsync(connectionString, id, "Merchant", "BRL");
                    await KnownDay.AddTransactionAsync(
                        connectionString,
                        Id(string.Create(CultureInfo.InvariantCulture, $"d0000000-0000-0000-0000-00000000000{merchant}")),
                        KnownDay.At(2026, 11, 20, 7, merchant, 0, 0),
                        (KnownDay.Card, "Debit", merchant * 1.25m, "BRL"),
                        (id, "Credit", merchant * 1.25m, "BRL"));
                }

                return (408, 6);

            case Scenario.DayWithoutRecords:
                await SeedAsync(connectionString, transactions: 30, merchants: 4, seed: 2, day: "2026-11-21");
                return (0, 0);

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    // Runs one strategy into a directory of its own and returns the bytes of the file, the only one left there.
    private async Task<byte[]> GenerateAsync(string connectionString, string strategy, int parallelism)
    {
        string parallelismText = parallelism.ToString(CultureInfo.InvariantCulture);
        string output = Path.Combine(_outputDirectory, $"{strategy}-{parallelismText}");

        int exitCode = await SettlementJobHost.RunAsync(
            [
                "run", "--strategy", strategy, "--parallelism", parallelismText,
                "--page-size", PageSize.ToString(CultureInfo.InvariantCulture), "--date", Day, "--output", output,
            ],
            builder => UseLedgerDatabase(builder, connectionString));

        Assert.Equal(ExitCodes.Success, exitCode);
        string file = Assert.Single(Directory.GetFiles(output));
        return await File.ReadAllBytesAsync(file);
    }

    private static async Task SeedAsync(string connectionString, int transactions, int merchants, int seed, string day = Day)
    {
        int exitCode = await SettlementJobHost.RunAsync(
            [
                "seed",
                "--transactions", transactions.ToString(CultureInfo.InvariantCulture),
                "--merchants", merchants.ToString(CultureInfo.InvariantCulture),
                "--cards", "10",
                "--date", day,
                "--seed", seed.ToString(CultureInfo.InvariantCulture),
            ],
            builder => UseLedgerDatabase(builder, connectionString));
        Assert.Equal(ExitCodes.Success, exitCode);
    }

    private static void UseLedgerDatabase(HostApplicationBuilder builder, string connectionString)
    {
        builder.Configuration[$"ConnectionStrings:{SettlementJobHost.LedgerConnectionName}"] = connectionString;
    }

    // The lines of the file, each checked to end with the line terminator.
    private static string[] Lines(byte[] file)
    {
        string content = Encoding.ASCII.GetString(file);
        Assert.EndsWith("\n", content, StringComparison.Ordinal);
        return content[..^1].Split('\n');
    }

    private static string[] Details(byte[] file)
    {
        return [.. Lines(file).Where(line => line.StartsWith('1'))];
    }

    private static Guid Id(string value)
    {
        return Guid.Parse(value, CultureInfo.InvariantCulture);
    }

    // Holds every query on the given merchant until it is cancelled; one that is not cancelled is released after the
    // limit and counted.
    private sealed class HeldQueries(Guid merchant, TimeSpan limit) : DbCommandInterceptor
    {
        private int _released;

        public int Released => Volatile.Read(ref _released);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid id && id == merchant))
            {
                await Task.Delay(limit, cancellationToken);
                Interlocked.Increment(ref _released);
            }

            return result;
        }
    }
}
