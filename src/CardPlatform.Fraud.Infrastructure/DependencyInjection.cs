using CardPlatform.Fraud.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CardPlatform.Fraud.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the connection string declared by the AppHost (<c>postgres.AddDatabase("frauddb")</c>).</summary>
    public const string ConnectionName = "frauddb";

    /// <summary>
    /// Registers the <see cref="FraudDbContext"/> through the Aspire PostgreSQL integration (connection string
    /// <c>frauddb</c>, health check, retries, telemetry).
    /// </summary>
    public static TBuilder AddFraudInfrastructure<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddNpgsqlDbContext<FraudDbContext>(
            ConnectionName,
            configureDbContextOptions: static options => FraudDbContextOptions.ApplyConventions(options));

        return builder;
    }

    /// <summary>Applies pending EF Core migrations to the Fraud database.</summary>
    public static async Task ApplyFraudMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        AsyncServiceScope scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            FraudDbContext dbContext = scope.ServiceProvider.GetRequiredService<FraudDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
