namespace CardPlatform.Messaging.Kafka;

/// <summary>How <see cref="KafkaTopicInitializer"/> creates topics (configuration section <c>Messaging:Kafka:Topics</c>).</summary>
public sealed class KafkaTopicOptions
{
    public const string SectionName = "Messaging:Kafka:Topics";

    /// <summary>
    /// Partitions per topic. Default 3: events are keyed by card id, so a card always lands on the same partition, and up
    /// to three consumers of a group can share a topic.
    /// </summary>
    public int Partitions { get; set; } = 3;

    /// <summary>Replicas per partition. Default 1, the single broker of the local environment.</summary>
    public short ReplicationFactor { get; set; } = 1;

    /// <summary>How long topic creation may take before startup goes on without it. Default 15 s.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}
