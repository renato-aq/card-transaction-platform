using Confluent.Kafka;

namespace CardPlatform.Messaging.Kafka;

/// <summary>Producer settings the outbox publisher relies on (ADR 0010).</summary>
public static class KafkaProducerDefaults
{
    /// <summary>
    /// Default delivery timeout (<c>message.timeout.ms</c>): how long a send may wait for the broker before it fails and
    /// the publisher backs off. librdkafka's default (5 minutes) would hold a publishing cycle for that long.
    /// </summary>
    public const int DefaultMessageTimeoutMs = 10_000;

    /// <summary>
    /// Forces an idempotent producer with <c>acks=all</c>: an acknowledged event is on every in-sync replica, and the
    /// producer's own retries neither duplicate nor reorder events within a partition. Sets a short delivery timeout
    /// unless one is configured (section <c>Aspire:Confluent:Kafka:Producer:Config</c>).
    /// </summary>
    public static void Apply(ProducerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.EnableIdempotence = true;
        config.Acks = Acks.All;
        config.MessageTimeoutMs ??= DefaultMessageTimeoutMs;
    }
}
