using CardPlatform.Fraud.Infrastructure;

using Npgsql;

using Testcontainers.PostgreSql;

namespace CardPlatform.Fraud.Worker.Tests.Infrastructure;

/// <summary>
/// A real PostgreSQL container shared by the worker tests. Each test gets its own database
/// (<see cref="CreateDatabaseAsync"/>), so what one host migrates is not seen by another.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    // Same major version as the PostgreSQL container started by the AppHost (Aspire 17).
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6")
        .WithDatabase(DependencyInjection.ConnectionName)
        .Build();

    public Task InitializeAsync()
    {
        return _postgres.StartAsync();
    }

    public Task DisposeAsync()
    {
        return _postgres.DisposeAsync().AsTask();
    }

    /// <summary>Creates an empty database in the container and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        string database = $"frauddb_{Guid.NewGuid():N}";

        NpgsqlDataSource dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await using (dataSource.ConfigureAwait(false))
        {
            NpgsqlCommand command = dataSource.CreateCommand($"CREATE DATABASE \"{database}\"");
            await using (command.ConfigureAwait(false))
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;
    }
}

/// <summary>The worker tests share one PostgreSQL and one Kafka container, and run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class WorkerCollectionFixture : ICollectionFixture<PostgreSqlFixture>, ICollectionFixture<KafkaFixture>
{
    public const string Name = "Worker";
}
