using Confluent.Kafka;

namespace CardPlatform.Messaging.Kafka;

/// <summary>Consumer settings the integration event consumer relies on (ADR 0011).</summary>
public static class KafkaConsumerDefaults
{
    /// <summary>
    /// Turns automatic offset commits off, since the consumer commits each offset itself after the message was processed
    /// and committed in the database. Unless configured (section <c>Aspire:Confluent:Kafka:Consumer:Config</c>), uses
    /// <paramref name="groupId"/> as the consumer group and starts a new group from the earliest offset, so a new consumer
    /// sees every event the topic still retains.
    /// </summary>
    public static void Apply(ConsumerConfig config, string groupId)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        config.EnableAutoCommit = false;
        config.GroupId ??= groupId;
        config.AutoOffsetReset ??= AutoOffsetReset.Earliest;
    }
}
