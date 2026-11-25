using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Infrastructure.Consuming;
using CardPlatform.Fraud.Infrastructure.Persistence;
using CardPlatform.Fraud.Infrastructure.Persistence.Repositories;
using CardPlatform.Messaging;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CardPlatform.Fraud.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Name of the connection string declared by the AppHost (<c>postgres.AddDatabase("frauddb")</c>).</summary>
    public const string ConnectionName = "frauddb";

    /// <summary>
    /// Default Kafka consumer group of the Fraud Worker; <c>Aspire:Confluent:Kafka:Consumer:Config:GroupId</c> overrides
    /// it.
    /// </summary>
    public const string ConsumerGroupId = "fraud-worker";

    /// <summary>
    /// Registers the <see cref="FraudDbContext"/> through the Aspire PostgreSQL integration (connection string
    /// <c>frauddb</c>, health check, retries, telemetry), the persistence adapters, and the consumer of the topic
    /// <c>authorizations</c> (connection string <c>kafka</c>, consumer group <see cref="ConsumerGroupId"/>, ADR 0011),
    /// which records every Authorization decision in the card's purchase history once per event id. In Development the
    /// topic is created on startup if it does not exist yet, so the Fraud Worker does not depend on starting after the
    /// Authorization.
    /// </summary>
    public static TBuilder AddFraudInfrastructure<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddNpgsqlDbContext<FraudDbContext>(
            ConnectionName,
            configureDbContextOptions: static options => FraudDbContextOptions.ApplyConventions(options));

        builder.Services.AddScoped<ICardPurchaseRepository, CardPurchaseRepository>();
        builder.Services.AddScoped<IUnitOfWork>(static services => services.GetRequiredService<FraudDbContext>());

        if (builder.Environment.IsDevelopment())
        {
            // Registered first, so the topic exists before the consumer subscribes to it.
            builder.AddKafkaTopicInitializer([Topics.Authorizations]);
        }

        builder.AddIntegrationEventConsumer<FraudDbContext>(ConsumerGroupId, [Topics.Authorizations]);
        builder.Services.AddIntegrationEventHandler<AuthorizationApproved, AuthorizationApprovedHandler>();
        builder.Services.AddIntegrationEventHandler<AuthorizationDeclined, AuthorizationDeclinedHandler>();
        builder.Services.AddIntegrationEventHandler<AuthorizationCaptured, AuthorizationCapturedHandler>();
        builder.Services.AddIntegrationEventHandler<AuthorizationVoided, AuthorizationVoidedHandler>();

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
