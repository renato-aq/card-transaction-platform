using CardPlatform.Ledger.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Testcontainers.PostgreSql;

namespace CardPlatform.Ledger.Api.Tests.Infrastructure;

/// <summary>
/// Starts the Ledger API against a real PostgreSQL container. The API runs in Development, so it applies
/// the EF Core migrations to the container on startup, which also exercises the migrations themselves.
/// </summary>
public sealed class LedgerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Same major version as the PostgreSQL container started by the AppHost (Aspire 17).
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6")
        .WithDatabase(DependencyInjection.ConnectionName)
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>A copy of this API, sharing the same database, that reads the time from <paramref name="clock"/>.</summary>
    public WebApplicationFactory<Program> WithClock(TimeProvider clock)
    {
        return WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(clock)));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionName}", _postgres.GetConnectionString());
    }
}

[CollectionDefinition(Name)]
public sealed class LedgerApiCollectionFixture : ICollectionFixture<LedgerApiFactory>
{
    public const string Name = "Ledger API";
}
