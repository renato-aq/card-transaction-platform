using CardPlatform.Authorization.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CardPlatform.Authorization.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the connection string declared by the AppHost (<c>postgres.AddDatabase("authorizationdb")</c>).</summary>
    public const string ConnectionName = "authorizationdb";

    /// <summary>
    /// Registers the <see cref="AuthorizationDbContext"/> through the Aspire PostgreSQL integration (connection string
    /// <c>authorizationdb</c>, health check, retries, telemetry).
    /// </summary>
    public static TBuilder AddAuthorizationInfrastructure<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddNpgsqlDbContext<AuthorizationDbContext>(
            ConnectionName,
            configureDbContextOptions: static options => AuthorizationDbContextOptions.ApplyConventions(options));

        return builder;
    }

    /// <summary>Applies pending EF Core migrations to the Authorization database.</summary>
    public static async Task ApplyAuthorizationMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        AsyncServiceScope scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
