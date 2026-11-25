using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Accounts;
using CardPlatform.Ledger.Application.Holds;
using CardPlatform.Ledger.Infrastructure.Persistence;
using CardPlatform.Ledger.Infrastructure.Persistence.Repositories;
using CardPlatform.Ledger.Infrastructure.Queries;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CardPlatform.Ledger.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the connection string declared by the AppHost (<c>postgres.AddDatabase("ledgerdb")</c>).</summary>
    public const string ConnectionName = "ledgerdb";

    /// <summary>
    /// Registers the <see cref="LedgerDbContext"/> through the Aspire PostgreSQL integration (connection string
    /// <c>ledgerdb</c>, health check, retries, telemetry), the persistence adapters and the query handlers, which
    /// read the database directly.
    /// </summary>
    public static TBuilder AddLedgerInfrastructure<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddNpgsqlDbContext<LedgerDbContext>(
            ConnectionName,
            configureDbContextOptions: static options => LedgerDbContextOptions.ApplyConventions(options));

        builder.Services.AddScoped<IAccountRepository, AccountRepository>();
        builder.Services.AddScoped<ILedgerTransactionRepository, LedgerTransactionRepository>();
        builder.Services.AddScoped<IHoldRepository, HoldRepository>();
        builder.Services.AddScoped<IUnitOfWork>(static services => services.GetRequiredService<LedgerDbContext>());

        builder.Services.AddScoped<IQueryHandler<GetAccountBalance, AccountBalanceDto>, GetAccountBalanceHandler>();
        builder.Services.AddScoped<IQueryHandler<GetHold, HoldDto>, GetHoldHandler>();

        return builder;
    }

    /// <summary>Applies pending EF Core migrations to the Ledger database.</summary>
    public static async Task ApplyLedgerMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        AsyncServiceScope scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            LedgerDbContext dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
            await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
