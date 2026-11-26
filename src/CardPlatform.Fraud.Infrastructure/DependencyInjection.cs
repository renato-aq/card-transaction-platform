using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Infrastructure.Consuming;
using CardPlatform.Fraud.Domain.Rules;
using CardPlatform.Fraud.Infrastructure.Persistence;
using CardPlatform.Fraud.Infrastructure.Persistence.Outbox;
using CardPlatform.Fraud.Infrastructure.Persistence.Repositories;
using CardPlatform.Fraud.Infrastructure.Rules;
using CardPlatform.Messaging;
using CardPlatform.Messaging.Consuming;
using CardPlatform.Messaging.Publishing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
    /// <c>frauddb</c>, health check, retries, telemetry), the persistence adapters (including the outbox), the
    /// <see cref="FraudRules"/> with the limits of <c>Fraud:Rules</c> (<see cref="FraudRulesOptions"/>, validated on
    /// startup), the consumer of the topic <c>authorizations</c> (connection string <c>kafka</c>, consumer group
    /// <see cref="ConsumerGroupId"/>, settings in <c>Messaging:Consumer</c>, ADR 0011), which records every Authorization
    /// decision in the card's purchase history once per event id and sends the messages it cannot process to
    /// <c>authorizations.dlt</c>, and the outbox publisher, which sends the <c>FraudSuspected</c> events to
    /// <c>fraud-signals</c> (settings in <c>Messaging:Outbox</c>, ADR 0010). With <c>Messaging:Consumer:Enabled</c> set to
    /// <see langword="false"/> the consumer is not registered and the decisions wait in Kafka; with
    /// <c>Messaging:Outbox:Enabled</c> set to <see langword="false"/> the suspicions stay in the outbox. In Development the
    /// topics it uses are created on startup if they do not exist yet, so the Fraud Worker does not depend on starting
    /// after the Authorization.
    /// </summary>
    public static TBuilder AddFraudInfrastructure<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddNpgsqlDbContext<FraudDbContext>(
            ConnectionName,
            configureDbContextOptions: static options => FraudDbContextOptions.ApplyConventions(options));

        builder.Services.AddScoped<ICardPurchaseRepository, CardPurchaseRepository>();
        builder.Services.AddScoped<IOutbox, EfOutbox>();
        builder.Services.AddScoped<IUnitOfWork>(static services => services.GetRequiredService<FraudDbContext>());
        builder.Services.TryAddSingleton(TimeProvider.System);

        builder.Services.AddOptions<FraudRulesOptions>()
            .BindConfiguration(FraudRulesOptions.SectionName)
            .Validate(static options => options.IsValid(), "Invalid fraud rule limits (Fraud:Rules).")
            .ValidateOnStart();
        builder.Services.AddSingleton(static services =>
            services.GetRequiredService<IOptions<FraudRulesOptions>>().Value.ToRules());

        bool publishes = OutboxPublisherOptions.IsEnabled(builder.Configuration);
        bool consumes = IntegrationEventConsumerOptions.IsEnabled(builder.Configuration);

        if (builder.Environment.IsDevelopment() && (publishes || consumes))
        {
            // Registered first, so the topics exist before the consumer subscribes and the publisher publishes: the
            // topic consumed and its dead-letter topic, and the topic published on.
            List<string> topics = [];
            if (consumes)
            {
                topics.Add(Topics.Authorizations);
                topics.Add(Topics.DeadLetterOf(Topics.Authorizations));
            }

            if (publishes)
            {
                topics.Add(Topics.FraudSignals);
            }

            builder.AddKafkaTopicInitializer(topics);
        }

        builder.AddIntegrationEventConsumer<FraudDbContext>(ConsumerGroupId, [Topics.Authorizations]);
        builder.Services.AddIntegrationEventHandler<AuthorizationApproved, AuthorizationApprovedHandler>();
        builder.Services.AddIntegrationEventHandler<AuthorizationDeclined, AuthorizationDeclinedHandler>();
        builder.Services.AddIntegrationEventHandler<AuthorizationCaptured, AuthorizationCapturedHandler>();
        builder.Services.AddIntegrationEventHandler<AuthorizationVoided, AuthorizationVoidedHandler>();

        if (publishes)
        {
            builder.AddOutboxPublisher<FraudDbContext>();
        }

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
