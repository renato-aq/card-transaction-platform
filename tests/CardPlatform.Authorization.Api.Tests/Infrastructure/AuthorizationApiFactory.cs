using CardPlatform.Authorization.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Testcontainers.PostgreSql;

namespace CardPlatform.Authorization.Api.Tests.Infrastructure;

/// <summary>
/// Starts the Authorization API against a real PostgreSQL container. The API runs in Development, so it applies
/// the EF Core migrations to the container on startup, which also exercises the migrations themselves.
/// </summary>
public sealed class AuthorizationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionName}", _postgres.GetConnectionString());
    }
}

[CollectionDefinition(Name)]
public sealed class AuthorizationApiCollectionFixture : ICollectionFixture<AuthorizationApiFactory>
{
    public const string Name = "Authorization API";
}
