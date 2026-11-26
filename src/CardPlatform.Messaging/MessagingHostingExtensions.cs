using Aspire.Confluent.Kafka;

using CardPlatform.Contracts;
using CardPlatform.Messaging.Consuming;
using CardPlatform.Messaging.Kafka;
using CardPlatform.Messaging.Publishing;

using Confluent.Kafka;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardPlatform.Messaging;

public static class MessagingHostingExtensions
{
    /// <summary>Default name of the Kafka connection string, declared by the AppHost (<c>builder.AddKafka("kafka")</c>).</summary>
    public const string DefaultKafkaConnectionName = "kafka";

    /// <summary>Configuration section of the Aspire Kafka producer settings.</summary>
    private const string KafkaProducerSettingsSection = "Aspire:Confluent:Kafka:Producer";

    /// <summary>Configuration section of the Aspire Kafka consumer settings.</summary>
    private const string KafkaConsumerSettingsSection = "Aspire:Confluent:Kafka:Consumer";

    /// <summary>
    /// Registers the outbox publisher of <typeparamref name="TDbContext"/> (whose model applies
    /// <see cref="Outbox.OutboxModelBuilderExtensions.ApplyOutbox"/>): a background service that publishes the outbox to
    /// Kafka (ADR 0010), with settings in <c>Messaging:Outbox</c> (<see cref="OutboxPublisherOptions"/>), and the Kafka
    /// producer through the Aspire integration (connection string <paramref name="connectionName"/>, telemetry; its health
    /// check is off unless configured), idempotent with <c>acks=all</c> (<see cref="KafkaProducerDefaults"/>). When
    /// <c>Messaging:Outbox:Enabled</c> is <see langword="false"/> nothing is registered and the events stay in the outbox.
    /// The context's database must be PostgreSQL (the publisher takes an advisory lock).
    /// </summary>
    public static IHostApplicationBuilder AddOutboxPublisher<TDbContext>(
        this IHostApplicationBuilder builder,
        string connectionName = DefaultKafkaConnectionName)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        if (!OutboxPublisherOptions.IsEnabled(builder.Configuration))
        {
            return builder;
        }

        builder.Services.AddOptions<OutboxPublisherOptions>()
            .BindConfiguration(OutboxPublisherOptions.SectionName)
            .Validate(static options => options.IsValid(), "Invalid outbox publisher settings (Messaging:Outbox).")
            .ValidateOnStart();

        AddKafkaProducerOnce(builder, connectionName);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IOutboxMessageProducer, KafkaOutboxMessageProducer>();
        builder.Services.TryAddSingleton<OutboxBatchPublisher<TDbContext>>();
        builder.Services.AddHostedService<OutboxPublisher<TDbContext>>();

        return builder;
    }

    /// <summary>
    /// Registers the integration event consumer of <typeparamref name="TDbContext"/> (whose model applies
    /// <see cref="ProcessedMessageModelBuilderExtensions.ApplyProcessedMessages"/>), ADR 0011: a background service that
    /// consumes <paramref name="topics"/> (or those of <c>Messaging:Consumer:Topics</c>, see
    /// <see cref="IntegrationEventConsumerOptions"/>) and processes each event once per event id with the handlers
    /// registered by <see cref="AddIntegrationEventHandler{TEvent, THandler}"/>, committing each offset after the database
    /// transaction. The Kafka consumer comes from the Aspire integration (connection string
    /// <paramref name="connectionName"/>, telemetry; its health check is off unless configured), in consumer group
    /// <paramref name="groupId"/> unless <c>Aspire:Confluent:Kafka:Consumer:Config:GroupId</c> says otherwise, without
    /// automatic commits and, for a new group, from the earliest offset (<see cref="KafkaConsumerDefaults"/>). One
    /// consumer per host. A message that fails with a temporary error is retried with backoff up to
    /// <c>Messaging:Consumer:MaxAttempts</c> times; one that keeps failing, or fails permanently, is sent to
    /// <c>&lt;topic&gt;.dlt</c> (<see cref="KafkaDeadLetterProducer"/>) through the Kafka producer of the Aspire
    /// integration, registered here too unless <see cref="AddOutboxPublisher{TDbContext}"/> already did. When
    /// <c>Messaging:Consumer:Enabled</c> is <see langword="false"/> nothing is registered and the events wait in Kafka. The
    /// context's database must be PostgreSQL.
    /// </summary>
    public static IHostApplicationBuilder AddIntegrationEventConsumer<TDbContext>(
        this IHostApplicationBuilder builder,
        string groupId,
        IEnumerable<string> topics,
        string connectionName = DefaultKafkaConnectionName)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        if (!IntegrationEventConsumerOptions.IsEnabled(builder.Configuration))
        {
            return builder;
        }

        string[] defaultTopics = [.. topics.Distinct(StringComparer.Ordinal)];

        builder.Services.AddOptions<IntegrationEventConsumerOptions>()
            .BindConfiguration(IntegrationEventConsumerOptions.SectionName)
            .PostConfigure(options =>
            {
                if (options.Topics.Count == 0)
                {
                    foreach (string topic in defaultTopics)
                    {
                        options.Topics.Add(topic);
                    }
                }
            })
            .Validate(static options => options.IsValid(), "Invalid integration event consumer settings (Messaging:Consumer).")
            .ValidateOnStart();

        // As for the producer, readiness does not depend on Kafka: an outage only delays processing, and the events
        // wait in Kafka. Set Aspire:Confluent:Kafka:Consumer:DisableHealthChecks to false to turn the check on.
        bool healthCheckConfigured =
            builder.Configuration[$"{KafkaConsumerSettingsSection}:{nameof(KafkaConsumerSettings.DisableHealthChecks)}"] is not null;

        // The group actually used (the configured one wins over groupId), for the dead-letter headers.
        string effectiveGroupId = groupId;
        builder.AddKafkaConsumer<string, byte[]>(connectionName, settings =>
        {
            KafkaConsumerDefaults.Apply(settings.Config, groupId);
            effectiveGroupId = settings.Config.GroupId;
            if (!healthCheckConfigured)
            {
                settings.DisableHealthChecks = true;
            }
        });

        // Messages that cannot be processed go to <topic>.dlt, also when the service does not publish an outbox.
        AddKafkaProducerOnce(builder, connectionName);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton(serviceProvider => new KafkaDeadLetterProducer(
            serviceProvider.GetRequiredService<IProducer<string, byte[]>>(),
            effectiveGroupId,
            serviceProvider.GetRequiredService<TimeProvider>()));
        builder.Services.TryAddSingleton<IntegrationEventProcessor<TDbContext>>();
        builder.Services.AddHostedService<IntegrationEventConsumer<TDbContext>>();

        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="THandler"/> (scoped) as the handler of the integration event
    /// <typeparamref name="TEvent"/>, called by the consumer of <see cref="AddIntegrationEventConsumer{TDbContext}"/>.
    /// Events with no handler are logged and ignored.
    /// </summary>
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        services.AddSingleton(IntegrationEventHandlerRegistration.For<TEvent>());

        return services;
    }

    /// <summary>
    /// Creates <paramref name="topics"/> on startup (<see cref="KafkaTopicInitializer"/>), with the partitions and
    /// replicas of <c>Messaging:Kafka:Topics</c> (<see cref="KafkaTopicOptions"/>). Meant for Development only; call it
    /// before registering the services that use the topics, so they are created first.
    /// </summary>
    public static IHostApplicationBuilder AddKafkaTopicInitializer(
        this IHostApplicationBuilder builder,
        IEnumerable<string> topics,
        string connectionName = DefaultKafkaConnectionName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        string[] names = [.. topics.Distinct(StringComparer.Ordinal)];

        builder.Services.AddOptions<KafkaTopicOptions>().BindConfiguration(KafkaTopicOptions.SectionName);
        builder.Services.AddHostedService(serviceProvider => new KafkaTopicInitializer(
            serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(connectionName)
                ?? throw new InvalidOperationException(
                    $"The Kafka connection string '{connectionName}' is missing (ConnectionStrings:{connectionName})."),
            names,
            serviceProvider.GetRequiredService<IOptions<KafkaTopicOptions>>().Value,
            serviceProvider.GetRequiredService<ILogger<KafkaTopicInitializer>>()));

        return builder;
    }

    /// <summary>
    /// Registers the Kafka producer through the Aspire integration (connection string <paramref name="connectionName"/>,
    /// telemetry; its health check is off unless configured), idempotent with <c>acks=all</c>
    /// (<see cref="KafkaProducerDefaults"/>), unless a producer is registered already: the outbox publisher and the
    /// consumer's dead-letter topic share one.
    /// </summary>
    private static void AddKafkaProducerOnce(IHostApplicationBuilder builder, string connectionName)
    {
        if (builder.Services.Any(static descriptor => descriptor.ServiceType == typeof(IProducer<string, byte[]>)))
        {
            return;
        }

        // Readiness must not depend on Kafka: an outage only delays events, which wait in the outbox (ADR 0010) or in
        // Kafka (ADR 0011). The Aspire health check stays available by setting
        // Aspire:Confluent:Kafka:Producer:DisableHealthChecks to false.
        bool healthCheckConfigured =
            builder.Configuration[$"{KafkaProducerSettingsSection}:{nameof(KafkaProducerSettings.DisableHealthChecks)}"] is not null;

        builder.AddKafkaProducer<string, byte[]>(connectionName, settings =>
        {
            KafkaProducerDefaults.Apply(settings.Config);
            if (!healthCheckConfigured)
            {
                settings.DisableHealthChecks = true;
            }
        });
    }
}