using CardPlatform.Settlement.Job.Tests.Infrastructure;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardPlatform.Settlement.Job.Tests.Seeding;

/// <summary>
/// Runs the <c>seed</c> command through the job's entry point (<see cref="SettlementJobHost"/>, the same code as
/// <c>Program</c>) with the connection string Aspire injects for <c>.WithReference(ledgerDb)</c>, and checks the exit code.
/// </summary>
[Collection(LedgerDatabaseCollectionFixture.Name)]
public sealed class SeedCommandTests(LedgerDatabaseFixture database)
{
    [Fact]
    public async Task Seed_command_loads_the_requested_transactions_and_exits_with_zero()
    {
        string connectionString = await database.CreateDatabaseAsync();

        int exitCode = await SettlementJobHost.RunAsync(
            ["seed", "--transactions", "300", "--merchants=5", "--cards", "10", "--date", "2026-11-20", "--seed", "3"],
            builder => UseLedgerDatabase(builder, connectionString));

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(
            300L,
            await LedgerDatabase.ScalarAsync<long>(
                connectionString,
                "SELECT count(*) FROM transactions WHERE created_at >= '2026-11-20T00:00:00Z' AND created_at < '2026-11-21T00:00:00Z'"));
    }

    [Fact]
    public async Task Seed_command_exits_with_a_failure_when_the_ledger_schema_does_not_exist()
    {
        string connectionString = await database.CreateDatabaseAsync(withLedgerSchema: false);
        using CapturedLogs logs = new();

        int exitCode = await SettlementJobHost.RunAsync(
            ["seed", "--transactions", "10"],
            builder =>
            {
                UseLedgerDatabase(builder, connectionString);
                builder.Logging.AddProvider(logs);
            });

        Assert.Equal(ExitCodes.Failure, exitCode);

        // The failure is the schema check's message, not some later error of the seed itself.
        CapturedLogEntry error = Assert.Single(logs.Errors);
        Assert.Contains("ledger schema was not found", error.Message, StringComparison.Ordinal);
        Assert.Contains("missing tables: accounts, transactions, entries", error.Message, StringComparison.Ordinal);
        Assert.Contains("Apply the Ledger migrations first", error.Message, StringComparison.Ordinal);
        Assert.Null(error.Exception);

        // Nothing was written: the database still has no tables at all.
        Assert.Equal(
            0L,
            await LedgerDatabase.ScalarAsync<long>(
                connectionString,
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = current_schema()"));
    }

    [Fact]
    public async Task Seed_command_exits_with_a_failure_when_the_ledger_connection_string_is_missing()
    {
        using CapturedLogs logs = new();

        int exitCode = await SettlementJobHost.RunAsync(
            ["seed", "--transactions", "10"],
            builder =>
            {
                // Blank out any connection string the environment could provide.
                builder.Configuration[$"ConnectionStrings:{SettlementJobHost.LedgerConnectionName}"] = string.Empty;
                builder.Configuration["Aspire:Npgsql:ConnectionString"] = string.Empty;
                builder.Logging.AddProvider(logs);
            });

        Assert.Equal(ExitCodes.Failure, exitCode);

        CapturedLogEntry error = Assert.Single(logs.Errors);
        Assert.Contains("connection string 'ledgerdb' is missing", error.Message, StringComparison.Ordinal);
        Assert.Contains("Aspire AppHost", error.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__ledgerdb", error.Message, StringComparison.Ordinal);
        Assert.Null(error.Exception);
    }

    private static void UseLedgerDatabase(HostApplicationBuilder builder, string connectionString)
    {
        builder.Configuration[$"ConnectionStrings:{SettlementJobHost.LedgerConnectionName}"] = connectionString;
    }
}
