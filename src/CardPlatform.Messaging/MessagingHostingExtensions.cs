using Aspire.Confluent.Kafka;

using CardPlatform.Messaging.Kafka;
using CardPlatform.Messaging.Publishing;

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

        // Readiness must not depend on Kafka: an outage only delays events, which wait in the outbox (ADR 0010). The
        // Aspire health check stays available by setting Aspire:Confluent:Kafka:Producer:DisableHealthChecks to false.
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

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IOutboxMessageProducer, KafkaOutboxMessageProducer>();
        builder.Services.TryAddSingleton<OutboxBatchPublisher<TDbContext>>();
        builder.Services.AddHostedService<OutboxPublisher<TDbContext>>();

        return builder;
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
}
