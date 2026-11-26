using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using CardPlatform.Authorization.Api.Tests.Infrastructure;
using CardPlatform.Authorization.Infrastructure;
using CardPlatform.Contracts;
using CardPlatform.Contracts.Fraud;
using CardPlatform.Messaging.Kafka;

using Confluent.Kafka;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Authorization.Api.Tests.Messaging;

/// <summary>
/// The Authorization's consumer of <c>fraud-signals</c> sends what it cannot process to <c>fraud-signals.dlt</c> and
/// goes on with the partition (spec 007, ADR 0011). In a collection of its own, with its own API and broker, because it
/// makes the consumer log errors, which <see cref="FraudSignalsConsumerTests"/> checks do not happen.
/// </summary>
[Collection(KafkaAuthorizationApiDeadLetterCollectionFixture.Name)]
public sealed class FraudSignalsDeadLetterTests(KafkaAuthorizationApiFactory factory)
{
    private const string ConsumerCategory = "CardPlatform.Messaging.Consuming.IntegrationEventConsumer";
    private const int DeadLetteredLogId = 120;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task A_malformed_message_goes_to_the_dead_letter_topic_and_the_next_message_of_its_partition_blocks_the_card()
    {
        Guid cardId = await CreateCardAsync();
        string key = cardId.ToString("D", CultureInfo.InvariantCulture);
        byte[] malformed = Encoding.UTF8.GetBytes("not an envelope");
        FraudSuspected suspected = new(cardId, Guid.CreateVersion7(), "velocity", "3 approved purchases.", DateTimeOffset.UtcNow);
        EventEnvelope envelope = EventEnvelope.Create(suspected, Guid.CreateVersion7(), suspected.DetectedAt);

        // Both on the same partition: the valid one is only consumed once the malformed one was dealt with.
        TopicPartitionOffset first = await PublishAsync(key, malformed);
        TopicPartitionOffset second = await PublishAsync(key, EventSerializer.SerializeToUtf8Bytes(envelope));

        await WaitUntilAsync(async () => await CardStatusAsync(cardId) == "blocked", $"Card {cardId} was not blocked.");
        await WaitUntilAsync(
            () => Task.FromResult(CommittedOffset(second.TopicPartition) > second.Offset.Value),
            $"The consumer did not confirm {second}.");

        Message<string, byte[]> deadLetter = Assert.Single(
            await ReadAllAsync(Topics.DeadLetterOf(Topics.FraudSignals)),
            message => Header(message, DeadLetterHeaders.OriginalOffset) == first.Offset.Value.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(key, deadLetter.Key);
        Assert.Equal(malformed, deadLetter.Value);
        Assert.Equal(Topics.FraudSignals, Header(deadLetter, DeadLetterHeaders.OriginalTopic));
        Assert.Equal("0", Header(deadLetter, DeadLetterHeaders.OriginalPartition));
        Assert.Equal(DeadLetterHeaders.PermanentFailure, Header(deadLetter, DeadLetterHeaders.FailureKind));
        Assert.Equal("1", Header(deadLetter, DeadLetterHeaders.Attempts));
        Assert.Equal(DependencyInjection.ConsumerGroupId, Header(deadLetter, DeadLetterHeaders.ConsumerGroup));

        CapturedLog logged = Assert.Single(
            factory.Logs.Entries,
            entry => entry.Category.StartsWith(ConsumerCategory, StringComparison.Ordinal)
                && entry.EventId == DeadLetteredLogId
                && entry.Values["Offset"] is long offset
                && offset == first.Offset.Value);
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Equal(Topics.DeadLetterOf(Topics.FraudSignals), logged.Values["DeadLetterTopic"]);
    }

    private async Task<TopicPartitionOffset> PublishAsync(string key, byte[] value)
    {
        _ = factory.Server; // The API creates fraud-signals and fraud-signals.dlt on startup (Development).

        using IProducer<string, byte[]> producer =
            new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = factory.KafkaBootstrapServers }).Build();
        DeliveryResult<string, byte[]> delivered = await producer.ProduceAsync(
            new TopicPartition(Topics.FraudSignals, 0),
            new Message<string, byte[]> { Key = key, Value = value });
        return delivered.TopicPartitionOffset;
    }

    private Task<List<Message<string, byte[]>>> ReadAllAsync(string topic)
    {
        return Task.Run(() =>
        {
            using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = factory.KafkaBootstrapServers }).Build();
            TopicPartition[] partitions =
            [
                .. admin.GetMetadata(topic, TimeSpan.FromSeconds(10)).Topics.Single().Partitions
                    .Select(partition => new TopicPartition(topic, partition.PartitionId)),
            ];

            using IConsumer<string, byte[]> consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
            {
                BootstrapServers = factory.KafkaBootstrapServers,
                GroupId = "test-reader-" + Guid.NewGuid().ToString("N"),
                EnableAutoCommit = false,
                EnablePartitionEof = true,
            }).Build();
            consumer.Assign(partitions.Select(static partition => new TopicPartitionOffset(partition, Offset.Beginning)));

            List<Message<string, byte[]>> messages = [];
            HashSet<int> ended = [];
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (ended.Count < partitions.Length && DateTime.UtcNow < deadline)
            {
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

    private long CommittedOffset(TopicPartition partition)
    {
        using IConsumer<string, byte[]> consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = factory.KafkaBootstrapServers,
            GroupId = DependencyInjection.ConsumerGroupId,
        }).Build();

        return consumer.Committed([partition], TimeSpan.FromSeconds(10)).Single().Offset.Value;
    }

    private static string Header(Message<string, byte[]> message, string key)
    {
        return message.Headers.TryGetLastBytes(key, out byte[] value) ? Encoding.UTF8.GetString(value) : string.Empty;
    }

    private async Task<string?> CardStatusAsync(Guid cardId)
    {
        JsonElement card = await _client.GetFromJsonAsync<JsonElement>($"/cards/{cardId}");
        return card.GetProperty("status").GetString();
    }

    private async Task<Guid> CreateCardAsync()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync("/cards", new { currency = "BRL" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string failure)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(
                DateTime.UtcNow < deadline,
                string.Create(CultureInfo.InvariantCulture, $"{failure} (waited {Timeout})."));
            await Task.Delay(100);
        }
    }
}
