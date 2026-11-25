using System.Globalization;

using CardPlatform.Authorization.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Testcontainers.PostgreSql;

namespace CardPlatform.Authorization.Api.Tests.Infrastructure;

/// <summary>
/// Starts the Authorization API against a real PostgreSQL container. The API runs in Development, so it applies
/// the EF Core migrations to the container on startup, which also exercises the migrations themselves.
/// <para>
/// The real Ledger API runs in process (<see cref="Ledger"/>) on the same container, in its own <c>ledgerdb</c>
/// database, and the Authorization's <c>ledger</c> <see cref="HttpClient"/> sends its requests straight to the Ledger's
/// test server: the calls go through the real client, resilience pipeline, HTTP contract and Ledger, without a network.
/// </para>
/// </summary>
public sealed class AuthorizationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Address the Ledger client resolves for https+http://ledger-api; the test server accepts any host.
    private const string LedgerAddress = "http://ledger.test";

    // Same major version as the PostgreSQL container started by the AppHost (Aspire 17).
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6")
        .WithDatabase(DependencyInjection.ConnectionName)
        .Build();

    private InProcessLedgerFactory? _ledger;

    /// <summary>The Ledger the Authorization API talks to, e.g. to check what a request created there.</summary>
    public InProcessLedgerFactory Ledger =>
        _ledger ?? throw new InvalidOperationException("The factory was not initialized.");

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        NpgsqlConnectionStringBuilder ledgerConnection = new(_postgres.GetConnectionString())
        {
            Database = InProcessLedgerFactory.ConnectionName,
        };
        _ledger = new InProcessLedgerFactory(ledgerConnection.ConnectionString);

        // Starts the Ledger (and creates and migrates its database) before any Authorization request reaches it.
        _ = _ledger.Server;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        if (_ledger is not null)
        {
            await _ledger.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// A copy of this API whose Ledger client cannot reach the Ledger (every request fails to connect), sharing the
    /// same database.
    /// </summary>
    public WebApplicationFactory<Program> WithLedgerDown()
    {
        return WithWebHostBuilder(static builder => builder.ConfigureTestServices(static services =>
            services.AddHttpClient(DependencyInjection.LedgerHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(static () => new UnreachableLedgerHandler())));
    }

    /// <summary>
    /// A copy of this API, sharing the same database, whose Ledger client reaches the Ledger but never gets the reply to
    /// placing a hold: the hold is placed, yet the client sees a dropped connection on every attempt.
    /// </summary>
    public WebApplicationFactory<Program> WithLedgerReplyLost()
    {
        return WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(DependencyInjection.LedgerHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new LostReplyLedgerHandler(Ledger.Server.CreateHandler()))));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionName}", _postgres.GetConnectionString());

        // What Aspire injects for .WithReference(ledgerApi).
        builder.UseSetting("services:ledger-api:http:0", LedgerAddress);

        // Generous timeouts: the in-process Ledger may be slow on its first (cold) requests. The production timeouts are
        // covered by the Ledger client tests.
        builder.UseSetting("Ledger:AttemptTimeout", TimeSpan.FromSeconds(5).ToString("c", CultureInfo.InvariantCulture));
        builder.UseSetting("Ledger:TotalRequestTimeout", TimeSpan.FromSeconds(10).ToString("c", CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
            services.AddHttpClient(DependencyInjection.LedgerHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Ledger.Server.CreateHandler()));
    }
}

[CollectionDefinition(Name)]
public sealed class AuthorizationApiCollectionFixture : ICollectionFixture<AuthorizationApiFactory>
{
    public const string Name = "Authorization API";
}
