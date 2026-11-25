using CardPlatform.Fraud.Infrastructure.Persistence;
using CardPlatform.Fraud.Worker.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CardPlatform.Fraud.Worker.Tests;

/// <summary>
/// Starts the real Fraud Worker host (<see cref="FraudWorkerHost"/>, the same composition root as <c>Program</c>)
/// against a PostgreSQL and a Kafka container, with the connection strings Aspire injects for
/// <c>.WithReference(fraudDb)</c> and <c>.WithReference(kafka)</c>.
/// </summary>
[Collection(WorkerCollectionFixture.Name)]
public sealed class FraudWorkerHostTests(PostgreSqlFixture postgres, KafkaFixture kafka)
{
    private const string InitialMigration = "InitialCreate";

    [Fact]
    public async Task In_development_the_host_starts_and_applies_the_initial_migration()
    {
        string connectionString = await postgres.CreateDatabaseAsync();

        using IHost host = WorkerHosts.Create(connectionString, kafka.BootstrapServers, Environments.Development);
        await FraudWorkerHost.InitializeAsync(host);
        await host.StartAsync();

        try
        {
            string[] applied = await AppliedMigrationsAsync(host);

            Assert.Contains(applied, static migration => migration.EndsWith($"_{InitialMigration}", StringComparison.Ordinal));
            Assert.Empty(await PendingMigrationsAsync(host));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Outside_development_the_host_starts_without_applying_migrations()
    {
        string connectionString = await postgres.CreateDatabaseAsync();

        using IHost host = WorkerHosts.Create(connectionString, kafka.BootstrapServers, Environments.Production);
        await FraudWorkerHost.InitializeAsync(host);
        await host.StartAsync();

        try
        {
            Assert.Empty(await AppliedMigrationsAsync(host));
            Assert.Contains(
                await PendingMigrationsAsync(host),
                static migration => migration.EndsWith($"_{InitialMigration}", StringComparison.Ordinal));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task<string[]> AppliedMigrationsAsync(IHost host)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        FraudDbContext dbContext = scope.ServiceProvider.GetRequiredService<FraudDbContext>();
        return [.. await dbContext.Database.GetAppliedMigrationsAsync()];
    }

    private static async Task<string[]> PendingMigrationsAsync(IHost host)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        FraudDbContext dbContext = scope.ServiceProvider.GetRequiredService<FraudDbContext>();
        return [.. await dbContext.Database.GetPendingMigrationsAsync()];
    }
}
