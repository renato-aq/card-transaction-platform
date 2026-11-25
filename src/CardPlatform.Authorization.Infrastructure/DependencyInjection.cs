using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Infrastructure.Ledger;
using CardPlatform.Authorization.Infrastructure.Persistence;
using CardPlatform.Authorization.Infrastructure.Persistence.Idempotency;
using CardPlatform.Authorization.Infrastructure.Persistence.Outbox;
using CardPlatform.Authorization.Infrastructure.Persistence.Repositories;
using CardPlatform.Authorization.Infrastructure.Queries;
using CardPlatform.Contracts;
using CardPlatform.Messaging;
using CardPlatform.Messaging.Publishing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace CardPlatform.Authorization.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the connection string declared by the AppHost (<c>postgres.AddDatabase("authorizationdb")</c>).</summary>
    public const string ConnectionName = "authorizationdb";

    /// <summary>Name of the <see cref="HttpClient"/> that talks to the Ledger.</summary>
    public const string LedgerHttpClientName = "ledger";

    /// <summary>
    /// Registers the <see cref="AuthorizationDbContext"/> through the Aspire PostgreSQL integration (connection string
    /// <c>authorizationdb</c>, health check, retries, telemetry), the persistence adapters (including the outbox, which
    /// needs a <see cref="TimeProvider"/> from the composition root), the query handlers, which read the database
    /// directly, the Ledger client and the outbox publisher, which sends the decisions written to the outbox to Kafka
    /// (connection string <c>kafka</c>, settings in <c>Messaging:Outbox</c>, see ADR 0010). In Development the Kafka
    /// topics the Authorization uses are created on startup. With <c>Messaging:Outbox:Enabled</c> set to
    /// <see langword="false"/> neither is registered and the decisions stay in the outbox.
    /// </summary>
    public static TBuilder AddAuthorizationInfrastructure<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddNpgsqlDbContext<AuthorizationDbContext>(
            ConnectionName,
            configureDbContextOptions: static options => AuthorizationDbContextOptions.ApplyConventions(options));

        builder.Services.AddScoped<ICardRepository, CardRepository>();
        builder.Services.AddScoped<IAuthorizationRepository, AuthorizationRepository>();
        builder.Services.AddScoped<IIdempotencyKeyStore, IdempotencyKeyStore>();
        builder.Services.AddScoped<IOutbox, EfOutbox>();
        builder.Services.AddScoped<IUnitOfWork>(static services => services.GetRequiredService<AuthorizationDbContext>());

        builder.Services.AddScoped<IQueryHandler<GetCard, CardDto>, GetCardHandler>();
        builder.Services.AddScoped<IQueryHandler<GetAuthorization, AuthorizationDto>, GetAuthorizationHandler>();

        builder.Services.AddLedgerClient();

        if (OutboxPublisherOptions.IsEnabled(builder.Configuration))
        {
            if (builder.Environment.IsDevelopment())
            {
                // The topic it publishes on and the one it will consume (fraud signals); registered first, so the
                // topics exist before the publisher starts.
                builder.AddKafkaTopicInitializer([Topics.Authorizations, Topics.FraudSignals]);
            }

            builder.AddOutboxPublisher<AuthorizationDbContext>();
        }

        return builder;
    }

    /// <summary>
    /// Registers <see cref="ILedgerClient"/> as a typed <see cref="HttpClient"/> (settings in the <c>Ledger</c>
    /// configuration section, see <see cref="LedgerClientOptions"/>). <c>ServiceDefaults</c> adds service discovery
    /// and a standard resilience handler with generic settings to every client; for the Ledger client that handler
    /// is replaced by a standard handler tuned for authorization calls: short timeouts and retries limited to the
    /// calls that are safe to repeat (see <see cref="LedgerRequestPolicy"/>). The global default is left untouched.
    /// </summary>
    public static IServiceCollection AddLedgerClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<LedgerClientOptions>().BindConfiguration(LedgerClientOptions.SectionName);

        // RemoveAllResilienceHandlers is experimental (EXTEXP0001), but it is the supported way to override, for one
        // client, the handler that ConfigureHttpClientDefaults adds to all of them.
#pragma warning disable EXTEXP0001
        services
            .AddHttpClient<ILedgerClient, LedgerHttpClient>(
                LedgerHttpClientName,
                static (serviceProvider, client) =>
                    client.BaseAddress = serviceProvider.GetRequiredService<IOptions<LedgerClientOptions>>().Value.BaseAddress)
            .RemoveAllResilienceHandlers()
            .AddStandardResilienceHandler()
            .Configure(static (resilience, serviceProvider) =>
                LedgerRequestPolicy.Configure(
                    resilience,
                    serviceProvider.GetRequiredService<IOptions<LedgerClientOptions>>().Value));
#pragma warning restore EXTEXP0001

        // Re-adding the handler appends it after service discovery; move it back to the outside, as ServiceDefaults
        // orders them, so each retry resolves the endpoint again and the timeouts cover the whole call.
        services.AddHttpClient(LedgerHttpClientName).ConfigureAdditionalHttpMessageHandlers(
            static (handlers, _) =>
            {
                List<DelegatingHandler> resilience = [.. handlers.Where(static handler => handler is ResilienceHandler)];
                foreach (DelegatingHandler handler in resilience)
                {
                    handlers.Remove(handler);
                }

                for (int index = resilience.Count - 1; index >= 0; index--)
                {
                    handlers.Insert(0, resilience[index]);
                }
            });

        return services;
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
