using CardPlatform.Contracts;

using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Testcontainers.Kafka;

namespace CardPlatform.Fraud.Worker.Tests.Infrastructure;

/// <summary>
/// A real Kafka broker shared by the worker tests. Each test that consumes publishes on its own topic
/// (<see cref="CreateTopicAsync"/>), which the worker is configured to consume instead of <c>authorizations</c>, so
/// what one test publishes is not seen by another.
/// </summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    /// <summary>Same image as the Kafka container started by the AppHost (Aspire 17).</summary>
    public const string KafkaImage = "confluentinc/confluent-local:8.4.0";

    private readonly KafkaContainer _kafka = new KafkaBuilder(KafkaImage).Build();

    /// <summary>The broker address for clients on the host (<c>host:port</c>), what Aspire injects as <c>ConnectionStrings:kafka</c>.</summary>
    public string BootstrapServers => _kafka.GetBootstrapAddress();

    public Task InitializeAsync()
    {
        return _kafka.StartAsync();
    }

    public Task DisposeAsync()
    {
        return _kafka.DisposeAsync().AsTask();
    }

    /// <summary>
    /// Creates a new topic with a unique name and <paramref name="partitions"/> partitions, and its dead-letter topic
    /// (<c>&lt;topic&gt;.dlt</c>), as the worker creates <c>authorizations</c> and <c>authorizations.dlt</c> in Development.
    /// </summary>
    public async Task<string> CreateTopicAsync(int partitions = 3)
    {
        string topic = "authorizations-" + Guid.NewGuid().ToString("N");
        using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = topic, NumPartitions = partitions, ReplicationFactor = 1 },
            new TopicSpecification { Name = Topics.DeadLetterOf(topic), NumPartitions = 1, ReplicationFactor = 1 },
        ]);
        return topic;
    }

    /// <summary>Publishes <paramref name="envelope"/> as the outbox publisher does: key = card id, value = the envelope.</summary>
    public Task PublishAsync(string topic, EventEnvelope envelope)
    {
        return PublishAsync(topic, envelope.PartitionKey, EventSerializer.SerializeToUtf8Bytes(envelope));
    }

    /// <summary>Publishes a raw message and waits for the broker's acknowledgement.</summary>
    public async Task PublishAsync(string topic, string key, byte[] value)
    {
        using IProducer<string, byte[]> producer =
            new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = BootstrapServers }).Build();
        await producer.ProduceAsync(topic, new Message<string, byte[]> { Key = key, Value = value });
    }

    /// <summary>
    /// Every message on <paramref name="topic"/> at this moment, read from the beginning of each partition to its end.
    /// </summary>
    public Task<List<Message<string, byte[]>>> ReadAllAsync(string topic)
    {
        return Task.Run(() =>
        {
            using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
            TopicPartition[] partitions =
            [
                .. admin.GetMetadata(topic, TimeSpan.FromSeconds(10)).Topics.Single().Partitions
                    .Select(partition => new TopicPartition(topic, partition.PartitionId)),
            ];

            using IConsumer<string, byte[]> consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
            {
                BootstrapServers = BootstrapServers,
                GroupId = "test-reader-" + Guid.NewGuid().ToString("N"),
                EnableAutoCommit = false,
                EnablePartitionEof = true,
            }).Build();
            consumer.Assign(partitions.Select(static partition => new TopicPartitionOffset(partition, Offset.Beginning)));

            List<Message<string, byte[]>> messages = [];
            HashSet<int> ended = [];
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (ended.Count < partitions.Length)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"Could not read {topic} to its end.");
                }

                ConsumeResult<string, byte[]>? result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (result is null)
                {
                    continue;
                }

                if (result.IsPartitionEOF)
                {
                    ended.Add(result.Partition.Value);
                }
                else
                {
                    messages.Add(result.Message);
                }
            }

            consumer.Close();
            return messages;
        });
    }

    /// <summary>The total of the offsets committed by <paramref name="groupId"/> on <paramref name="topic"/>: how many messages it confirmed.</summary>
    public long CommittedCount(string groupId, string topic)
    {
        using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        Metadata metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(10));

        using IConsumer<string, byte[]> consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = groupId,
        }).Build();

        return consumer
            .Committed(
                metadata.Topics.Single().Partitions.Select(partition => new TopicPartition(topic, partition.PartitionId)),
                TimeSpan.FromSeconds(10))
            .Where(static offset => offset.Offset.Value > 0)
            .Sum(static offset => offset.Offset.Value);
    }
}
