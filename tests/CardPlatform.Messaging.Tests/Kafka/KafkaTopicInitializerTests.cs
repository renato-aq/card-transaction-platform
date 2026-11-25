using CardPlatform.Messaging.Kafka;
using CardPlatform.Messaging.Tests.Infrastructure;

using Confluent.Kafka;

using Microsoft.Extensions.Logging.Abstractions;

namespace CardPlatform.Messaging.Tests.Kafka;

[Collection(MessagingCollectionFixture.Name)]
public sealed class KafkaTopicInitializerTests(MessagingFixture fixture)
{
    [Fact]
    public async Task Creates_the_topics_with_the_configured_partitions_and_tolerates_existing_ones()
    {
        string[] topics = ["init-" + Guid.NewGuid().ToString("N"), "init-" + Guid.NewGuid().ToString("N")];
        KafkaTopicInitializer initializer = new(
            fixture.BootstrapServers,
            topics,
            new KafkaTopicOptions { Partitions = 3 },
            NullLogger<KafkaTopicInitializer>.Instance);

        await initializer.StartAsync(CancellationToken.None);
        await initializer.StartAsync(CancellationToken.None);

        using IAdminClient admin =
            new AdminClientBuilder(new AdminClientConfig { BootstrapServers = fixture.BootstrapServers }).Build();
        foreach (string topic in topics)
        {
            TopicMetadata metadata = Assert.Single(admin.GetMetadata(topic, TimeSpan.FromSeconds(10)).Topics);
            Assert.Equal(ErrorCode.NoError, metadata.Error.Code);
            Assert.Equal(3, metadata.Partitions.Count);
        }
    }
}
