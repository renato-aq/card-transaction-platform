using CardPlatform.Ledger.Infrastructure;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using Npgsql;

using Testcontainers.PostgreSql;

namespace CardPlatform.Settlement.Job.Tests.Infrastructure;

/// <summary>
/// A real PostgreSQL container shared by the seed tests. Each test gets its own database (<see cref="CreateDatabaseAsync"/>),
/// with the ledger schema created by the Ledger's own EF Core migrations, so the seed is checked against the real tables.
/// </summary>
public sealed class LedgerDatabaseFixture : IAsyncLifetime
{
    // Same major version as the PostgreSQL container started by the AppHost (Aspire 17).
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6")
        .WithDatabase(SettlementJobHost.LedgerConnectionName)
        .Build();

    public Task InitializeAsync()
    {
        return _postgres.StartAsync();
    }

    public Task DisposeAsync()
    {
        return _postgres.DisposeAsync().AsTask();
    }

    /// <summary>
    /// Creates a database in the container and returns its connection string; with <paramref name="withLedgerSchema"/>,
    /// applies the Ledger migrations to it, otherwise it stays empty.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(bool withLedgerSchema = true)
    {
        string database = $"ledgerdb_{Guid.NewGuid():N}";

        NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await using (dataSource.ConfigureAwait(false))
        {
            NpgsqlCommand command = dataSource.CreateCommand($"CREATE DATABASE \"{database}\"");
            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        string connectionString =
            new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;

        if (withLedgerSchema)
        {
            await ApplyLedgerMigrationsAsync(connectionString).ConfigureAwait(false);
        }

        return connectionString;
    }

    private static async Task ApplyLedgerMigrationsAsync(string connectionString)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
        builder.Configuration.AddInMemoryCollection(
            [new($"ConnectionStrings:{DependencyInjection.ConnectionName}", connectionString)]);
        builder.AddLedgerInfrastructure();

        using IHost host = builder.Build();
        await host.Services.ApplyLedgerMigrationsAsync().ConfigureAwait(false);
    }
}

/// <summary>The seed tests share one PostgreSQL container.</summary>
[CollectionDefinition(Name)]
public sealed class LedgerDatabaseCollectionFixture : ICollectionFixture<LedgerDatabaseFixture>
{
    public const string Name = "Ledger database";
}
