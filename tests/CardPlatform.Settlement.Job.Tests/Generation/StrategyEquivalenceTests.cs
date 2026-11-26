using System.Globalization;
using System.Text;

using CardPlatform.Settlement.Job.Tests.Infrastructure;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace CardPlatform.Settlement.Job.Tests.Generation;

/// <summary>
/// The <c>optimized</c> strategy (streaming, keyset pages) must write exactly the file of the <c>naive</c> one (version
/// 1) for the same data. Both run through the job's entry point against the same database, and their files are compared
/// byte by byte, with page sizes small enough that pages end inside a merchant and inside a transaction.
/// </summary>
[Collection(LedgerDatabaseCollectionFixture.Name)]
public sealed class StrategyEquivalenceTests(LedgerDatabaseFixture database) : IDisposable
{
    private const int RecordLength = 117;

    private const string Day = "2026-11-20";

    private const string EfCommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), $"settlement-equivalence-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_strategies_write_the_same_file_for_a_day_without_records(bool withOtherDays)
    {
        string connectionString = await database.CreateDatabaseAsync();
        if (withOtherDays)
        {
            await KnownDay.ArrangeAsync(connectionString);
        }

        (byte[] naive, byte[] optimized) = await GenerateWithBothStrategiesAsync(connectionString, "2026-11-22", pageSize: 2);

        Assert.Equal(naive, optimized);
        Assert.Equal(
            Lines(Record("020261122BRL"), Record("9000000000000000000000000000")),
            Encoding.ASCII.GetString(optimized));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Both_strategies_write_the_same_file_for_a_single_record(int pageSize)
    {
        string connectionString = await database.CreateDatabaseAsync();
        Guid merchant = Id("10000000-0000-0000-0000-000000000001");
        Guid card = Id("c0000000-0000-0000-0000-000000000001");
        Guid purchase = Id("a0000000-0000-0000-0000-000000000001");
        await KnownDay.AddAccountAsync(connectionString, merchant, "Merchant", "BRL");
        await KnownDay.AddAccountAsync(connectionString, card, "Card", "BRL");
        await KnownDay.AddTransactionAsync(
            connectionString,
            purchase,
            KnownDay.At(2026, 11, 20, 13, 30, 0, 42),
            (card, "Debit", 12.34m, "BRL"),
            (merchant, "Credit", 12.34m, "BRL"));

        (byte[] naive, byte[] optimized) = await GenerateWithBothStrategiesAsync(connectionString, Day, pageSize);

        Assert.Equal(naive, optimized);
        Assert.Equal(
            Lines(
                Record("020261120BRL"),
                Detail(1, purchase, merchant, "000000000001234", "20261120133000000042"),
                Record("9000000001000000000000001234")),
            Encoding.ASCII.GetString(optimized));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(10_000)]
    public async Task Both_strategies_write_the_same_file_for_several_merchants(int pageSize)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, transactions: 300, merchants: 7, seed: 5);
        await KnownDay.ArrangeAsync(connectionString);

        (byte[] naive, byte[] optimized) = await GenerateWithBothStrategiesAsync(connectionString, Day, pageSize);

        Assert.Equal(naive, optimized);
        Assert.Equal(305, DetailCount(optimized));
        Assert.Equal(9, MerchantCount(optimized)); // the 7 seeded and the 2 BRL merchants of the known day
    }

    // A single merchant, so its records are read in pages of pageSize: the last page is full (a multiple), partial
    // (less than a multiple) or holds one record (more than a multiple), and a merchant can fit in less than a page.
    [Theory]
    [InlineData(12, 4)]
    [InlineData(11, 4)]
    [InlineData(13, 4)]
    [InlineData(4, 4)]
    [InlineData(3, 4)]
    [InlineData(5, 4)]
    public async Task Both_strategies_write_the_same_file_whether_the_records_are_a_multiple_of_the_page_size_or_not(
        int records,
        int pageSize)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, records, merchants: 1, seed: records);

        (byte[] naive, byte[] optimized) = await GenerateWithBothStrategiesAsync(connectionString, Day, pageSize);

        Assert.Equal(naive, optimized);
        Assert.Equal(records, DetailCount(optimized));
    }

    // The merchant accounts are read in pages too: their count is a multiple of the page size, less or more.
    [Theory]
    [InlineData(4, 2)]
    [InlineData(3, 2)]
    [InlineData(5, 2)]
    public async Task Both_strategies_write_the_same_file_whether_the_merchants_are_a_multiple_of_the_page_size_or_not(
        int merchants,
        int pageSize)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, transactions: 60, merchants, seed: merchants);

        (byte[] naive, byte[] optimized) = await GenerateWithBothStrategiesAsync(connectionString, Day, pageSize);

        Assert.Equal(naive, optimized);
        Assert.Equal(60, DetailCount(optimized));
        Assert.Equal(merchants, MerchantCount(optimized));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task Pages_neither_skip_nor_repeat_an_entry_at_their_boundaries(int pageSize)
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, transactions: 200, merchants: 3, seed: 9);
        await KnownDay.ArrangeAsync(connectionString);

        // Every settlement entry of the day, in the order of the file, as the database sorts it.
        List<string> expected = await LedgerDatabase.ListAsync(
            connectionString,
            """
            SELECT e.transaction_id, e.account_id, e.amount
            FROM entries e
            JOIN accounts a ON a.id = e.account_id
            JOIN transactions t ON t.id = e.transaction_id
            WHERE e.direction = 'Credit' AND a.type = 'Merchant' AND e.currency = 'BRL'
              AND t.created_at >= '2026-11-20T00:00:00Z' AND t.created_at < '2026-11-21T00:00:00Z'
            ORDER BY e.account_id, e.transaction_id, e.id
            """,
            reader => string.Create(
                CultureInfo.InvariantCulture,
                $"{reader.GetGuid(0):D}{reader.GetGuid(1):D}{reader.GetDecimal(2) * 100m:000000000000000}"));
        Assert.Equal(205, expected.Count);

        (byte[] naive, byte[] optimized) = await GenerateWithBothStrategiesAsync(connectionString, Day, pageSize);

        string[] details = Details(optimized);
        Assert.Equal(expected, details.Select(line => line[10..97]));
        Assert.Equal(
            Enumerable.Range(1, expected.Count).Select(number => number.ToString("D9", CultureInfo.InvariantCulture)),
            details.Select(line => line[1..10]));
        Assert.Equal(naive, optimized);
    }

    // Entry ids are the third key of the order: two or more credits of one transaction to one merchant are written by
    // entry id, not in the order they were inserted, including when a page ends between them.
    [Theory]
    [InlineData("naive", 10_000)]
    [InlineData("optimized", 1)]
    [InlineData("optimized", 2)]
    [InlineData("optimized", 3)]
    [InlineData("optimized", 4)]
    [InlineData("optimized", 10_000)]
    public async Task Credits_of_one_transaction_to_one_merchant_follow_the_entry_id_and_not_the_insertion_order(
        string strategy,
        int pageSize)
    {
        string connectionString = await database.CreateDatabaseAsync();
        string expected = await ArrangeCreditsOfOneTransactionToOneMerchantAsync(connectionString);

        byte[] file = await GenerateAsync(connectionString, strategy, Day, pageSize);

        Assert.Equal(expected, Encoding.ASCII.GetString(file));
    }

    // The same credits, read with index scans disabled, so PostgreSQL sorts the candidate rows for every page instead of
    // returning them in the order of the index; equal sort keys then come out in no particular order. Only the entry id
    // in the ORDER BY of each page (the first one and the keyed ones) keeps the credits of one transaction in order and
    // makes a page start right after the last entry read, without skipping or repeating one.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(10_000)]
    public async Task Credits_of_one_transaction_to_one_merchant_follow_the_entry_id_when_the_database_sorts_each_page(
        int pageSize)
    {
        string connectionString = await database.CreateDatabaseAsync();
        string expected = await ArrangeCreditsOfOneTransactionToOneMerchantAsync(connectionString);
        string sortingConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Options = "-c enable_indexscan=off -c enable_bitmapscan=off",
        }.ConnectionString;

        byte[] file = await GenerateAsync(sortingConnectionString, "optimized", Day, pageSize);

        Assert.Equal(expected, Encoding.ASCII.GetString(file));
    }

    [Fact]
    public async Task The_optimized_strategy_reads_pages_with_a_keyset_and_a_limit_and_never_an_offset()
    {
        string connectionString = await database.CreateDatabaseAsync();
        await SeedAsync(connectionString, transactions: 60, merchants: 5, seed: 13);
        using CapturedLogs logs = new();

        int exitCode = await SettlementJobHost.RunAsync(
            ["run", "--strategy", "optimized", "--page-size", "4", "--date", Day, "--output", _outputDirectory],
            builder =>
            {
                UseLedgerDatabase(builder, connectionString);
                builder.Logging.AddProvider(logs);
                builder.Logging.AddFilter(EfCommandCategory, LogLevel.Information);
            });

        Assert.Equal(ExitCodes.Success, exitCode);
        string[] commands =
        [
            .. logs.Entries
                .Where(entry => entry.Category == EfCommandCategory
                    && entry.Message.StartsWith("Executed DbCommand", StringComparison.Ordinal))
                .Select(entry => entry.Message),
        ];

        // 60 records of 5 merchants in pages of 4: at least 15 pages of entries, and 2 pages of merchant ids.
        Assert.True(commands.Length >= 17, $"Only {commands.Length} queries were executed.");
        Assert.All(commands, command => Assert.Contains("LIMIT", command, StringComparison.Ordinal));
        Assert.All(commands, command => Assert.DoesNotContain("OFFSET", command, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands, command => command.Contains("(a.id) > (@", StringComparison.Ordinal));
        Assert.Contains(
            commands,
            command => command.Contains("e.account_id = @", StringComparison.Ordinal)
                && command.Contains("AND (e.transaction_id, e.id) > (@", StringComparison.Ordinal));
    }

    private async Task<(byte[] Naive, byte[] Optimized)> GenerateWithBothStrategiesAsync(
        string connectionString,
        string date,
        int pageSize)
    {
        byte[] naive = await GenerateAsync(connectionString, "naive", date, pageSize);
        byte[] optimized = await GenerateAsync(connectionString, "optimized", date, pageSize);
        return (naive, optimized);
    }

    // Runs one strategy into a directory of its own and returns the bytes of the file, the only one left there.
    private async Task<byte[]> GenerateAsync(string connectionString, string strategy, string date, int pageSize)
    {
        string pageSizeText = pageSize.ToString(CultureInfo.InvariantCulture);
        string output = Path.Combine(_outputDirectory, $"{strategy}-{pageSizeText}");

        int exitCode = await SettlementJobHost.RunAsync(
            ["run", "--strategy", strategy, "--page-size", pageSizeText, "--date", date, "--output", output],
            builder => UseLedgerDatabase(builder, connectionString));

        Assert.Equal(ExitCodes.Success, exitCode);
        string file = Assert.Single(Directory.GetFiles(output));
        return await File.ReadAllBytesAsync(file);
    }

    private static async Task SeedAsync(string connectionString, int transactions, int merchants, int seed)
    {
        int exitCode = await SettlementJobHost.RunAsync(
            [
                "seed",
                "--transactions", transactions.ToString(CultureInfo.InvariantCulture),
                "--merchants", merchants.ToString(CultureInfo.InvariantCulture),
                "--cards", "10",
                "--date", Day,
                "--seed", seed.ToString(CultureInfo.InvariantCulture),
            ],
            builder => UseLedgerDatabase(builder, connectionString));
        Assert.Equal(ExitCodes.Success, exitCode);
    }

    // One merchant credited by three transactions, the middle one (by id) split into three credits inserted with the
    // highest entry id first. Returns the file the day must produce: the split credits by entry id (1.00, 2.00, 3.00).
    private static async Task<string> ArrangeCreditsOfOneTransactionToOneMerchantAsync(string connectionString)
    {
        Guid merchant = Id("20000000-0000-0000-0000-000000000002");
        Guid card = Id("c0000000-0000-0000-0000-000000000001");
        Guid earlierPurchase = Id("01000000-0000-0000-0000-000000000001");
        Guid splitPurchase = Id("50000000-0000-0000-0000-000000000005");
        Guid laterPurchase = Id("f0000000-0000-0000-0000-000000000009");
        await KnownDay.AddAccountAsync(connectionString, merchant, "Merchant", "BRL");
        await KnownDay.AddAccountAsync(connectionString, card, "Card", "BRL");
        await KnownDay.AddTransactionAsync(
            connectionString, laterPurchase, KnownDay.At(2026, 11, 20, 9, 0, 0, 0),
            (card, "Debit", 0.70m, "BRL"), (merchant, "Credit", 0.70m, "BRL"));
        await KnownDay.AddTransactionAsync(connectionString, splitPurchase, KnownDay.At(2026, 11, 20, 10, 0, 0, 0));
        await KnownDay.AddEntryAsync(connectionString, 1_000_000, splitPurchase, card, "Debit", 6.00m, "BRL");

        // Inserted with the highest entry id first: the file must list them by entry id (1.00, 2.00, 3.00).
        await KnownDay.AddEntryAsync(connectionString, 1_000_003, splitPurchase, merchant, "Credit", 3.00m, "BRL");
        await KnownDay.AddEntryAsync(connectionString, 1_000_002, splitPurchase, merchant, "Credit", 2.00m, "BRL");
        await KnownDay.AddEntryAsync(connectionString, 1_000_001, splitPurchase, merchant, "Credit", 1.00m, "BRL");
        await KnownDay.AddTransactionAsync(
            connectionString, earlierPurchase, KnownDay.At(2026, 11, 20, 11, 0, 0, 0),
            (card, "Debit", 0.50m, "BRL"), (merchant, "Credit", 0.50m, "BRL"));

        return Lines(
            Record("020261120BRL"),
            Detail(1, earlierPurchase, merchant, "000000000000050", "20261120110000000000"),
            Detail(2, splitPurchase, merchant, "000000000000100", "20261120100000000000"),
            Detail(3, splitPurchase, merchant, "000000000000200", "20261120100000000000"),
            Detail(4, splitPurchase, merchant, "000000000000300", "20261120100000000000"),
            Detail(5, laterPurchase, merchant, "000000000000070", "20261120090000000000"),
            Record("9000000005000000000000000720"));
    }

    private static void UseLedgerDatabase(HostApplicationBuilder builder, string connectionString)
    {
        builder.Configuration[$"ConnectionStrings:{SettlementJobHost.LedgerConnectionName}"] = connectionString;
    }

    private static string[] Details(byte[] file)
    {
        return [.. Encoding.ASCII.GetString(file).Split('\n').Where(line => line.StartsWith('1'))];
    }

    private static int DetailCount(byte[] file)
    {
        return Details(file).Length;
    }

    private static int MerchantCount(byte[] file)
    {
        return Details(file).Select(line => line[46..82]).Distinct(StringComparer.Ordinal).Count();
    }

    private static string Lines(params string[] records)
    {
        return string.Concat(records.Select(record => record + "\n"));
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

    private static Guid Id(string value)
    {
        return Guid.Parse(value, CultureInfo.InvariantCulture);
    }
}
