extern alias FraudWorker;

using CardPlatform.Messaging;

using Microsoft.Extensions.Hosting;

using Npgsql;

using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

using FraudWorker::CardPlatform.Fraud.Worker;

using AuthorizationInfrastructure = CardPlatform.Authorization.Infrastructure.DependencyInjection;
using FraudInfrastructure = CardPlatform.Fraud.Infrastructure.DependencyInjection;

namespace CardPlatform.EndToEnd.Tests.Infrastructure;

/// <summary>
/// The platform as the AppHost runs it, in process: one PostgreSQL container with a database per service
/// (<c>ledgerdb</c>, <c>authorizationdb</c>, <c>frauddb</c>), one Kafka container, the real Ledger and Authorization
/// APIs and the real Fraud Worker host. Every service runs in Development, so each one migrates its own database and
/// creates the topics it uses on startup.
/// <para>
/// The Fraud Worker's velocity rule is lowered by configuration (<see cref="VelocityThreshold"/> approved purchases
/// within a minute), so a few purchases are enough to raise a suspicion.
/// </para>
/// </summary>
public sealed class PlatformFixture : IAsyncLifetime, IAsyncDisposable
{
    /// <summary>How many approved purchases of a card within <see cref="VelocityWindow"/> fire the velocity rule.</summary>
    public const int VelocityThreshold = 3;

    public static readonly TimeSpan VelocityWindow = TimeSpan.FromMinutes(1);

    // Same images as the containers started by the AppHost (Aspire 17).
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6")
        .WithDatabase(AuthorizationInfrastructure.ConnectionName)
        .Build();

    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/confluent-local:8.4.0").Build();

    private InProcessLedgerFactory? _ledger;
    private AuthorizationApiFactory? _authorization;
    private IHost? _fraudWorker;

    public InProcessLedgerFactory Ledger =>
        _ledger ?? throw new InvalidOperationException("The fixture was not initialized.");

    public AuthorizationApiFactory Authorization =>
        _authorization ?? throw new InvalidOperationException("The fixture was not initialized.");

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());

        _ledger = new InProcessLedgerFactory(ConnectionStringOf(InProcessLedgerFactory.ConnectionName));
        _ = _ledger.Server;

        _authorization = new AuthorizationApiFactory(
            _postgres.GetConnectionString(),
            _kafka.GetBootstrapAddress(),
            _ledger);
        _ = _authorization.Server;

        _fraudWorker = FraudWorkerHost.CreateBuilder(
        [
            $"--environment={Environments.Development}",
            $"--ConnectionStrings:{FraudInfrastructure.ConnectionName}={ConnectionStringOf(FraudInfrastructure.ConnectionName)}",
            $"--ConnectionStrings:{MessagingHostingExtensions.DefaultKafkaConnectionName}={_kafka.GetBootstrapAddress()}",
            $"--Fraud:Rules:Velocity:Threshold={VelocityThreshold}",
            $"--Fraud:Rules:Velocity:Window={VelocityWindow:c}",
            "--Messaging:Outbox:PollingInterval=00:00:00.100",
            "--Messaging:Consumer:RetryInitialDelay=00:00:00.200",
            "--Messaging:Consumer:RetryMaxDelay=00:00:01",
        ]).Build();
        await FraudWorkerHost.InitializeAsync(_fraudWorker);
        await _fraudWorker.StartAsync();
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return DisposeAsync().AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        if (_fraudWorker is not null)
        {
            await _fraudWorker.StopAsync();
            _fraudWorker.Dispose();
            _fraudWorker = null;
        }

        if (_authorization is not null)
        {
            await _authorization.DisposeAsync();
            _authorization = null;
        }

        if (_ledger is not null)
        {
            await _ledger.DisposeAsync();
            _ledger = null;
        }

        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _kafka.DisposeAsync().AsTask());
    }

    /// <summary>The connection string of <paramref name="database"/> in the container (created by the service on startup).</summary>
    private string ConnectionStringOf(string database)
    {
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = database }.ConnectionString;
    }
}

[CollectionDefinition(Name)]
public sealed class PlatformCollectionFixture : ICollectionFixture<PlatformFixture>
{
    public const string Name = "Platform";
}
