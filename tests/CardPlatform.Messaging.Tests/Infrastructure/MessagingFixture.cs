using System.Globalization;

using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Messaging.Consuming;
using CardPlatform.Messaging.Outbox;

using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

namespace CardPlatform.Messaging.Tests.Infrastructure;

/// <summary>
/// A PostgreSQL container with an <c>outbox_messages</c> table (<see cref="TestOutboxDbContext"/>) and a Kafka broker,
/// shared by the tests of the <see cref="MessagingCollectionFixture"/>, which run one at a time. Each test empties the outbox
/// (<see cref="ResetOutboxAsync"/>) and publishes on its own topic (<see cref="CreateTopicAsync"/>).
/// </summary>
public sealed class MessagingFixture : IAsyncLifetime
{
    /// <summary>Same image as the Kafka container started by the AppHost (Aspire 17).</summary>
    public const string KafkaImage = "confluentinc/confluent-local:8.4.0";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").Build();

    public KafkaContainer Kafka { get; } = new KafkaBuilder(KafkaImage).Build();

    /// <summary>The broker address for clients on the host (<c>host:port</c>); pausing the container keeps it.</summary>
    public string BootstrapServers => Kafka.GetBootstrapAddress();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), Kafka.StartAsync());

        await using TestOutboxDbContext dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await Kafka.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public TestOutboxDbContext CreateDbContext()
    {
        DbContextOptionsBuilder<TestOutboxDbContext> options = new();
        TestOutboxDbContext.Configure(options, ConnectionString);
        return new TestOutboxDbContext(options.Options);
    }

    public async Task ResetOutboxAsync()
    {
        await using TestOutboxDbContext dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE outbox_messages, processed_messages RESTART IDENTITY");
    }

    /// <summary>
    /// Creates a new topic with a unique name and <paramref name="partitions"/> partitions, and its dead-letter topic
    /// (<c>&lt;topic&gt;.dlt</c>, one partition).
    /// </summary>
    public async Task<string> CreateTopicAsync(int partitions = 3)
    {
        string topic = "test-" + Guid.NewGuid().ToString("N");
        using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = topic, NumPartitions = partitions, ReplicationFactor = 1 },
            new TopicSpecification { Name = Topics.DeadLetterOf(topic), NumPartitions = 1, ReplicationFactor = 1 },
        ]);
        return topic;
    }

    /// <summary>
    /// A host (not started) with the outbox publisher registered as a service would register it, for
    /// <see cref="TestOutboxDbContext"/>, with short intervals and delivery timeout for tests.
    /// </summary>
    public IHost CreatePublisherHost(Action<IServiceCollection>? configureServices = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Production });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{MessagingHostingExtensions.DefaultKafkaConnectionName}"] = BootstrapServers,
            ["Messaging:Outbox:PollingInterval"] = "00:00:00.100",
            ["Messaging:Outbox:RetryInitialDelay"] = "00:00:00.200",
            ["Messaging:Outbox:RetryMaxDelay"] = "00:00:01",
            ["Aspire:Confluent:Kafka:Producer:Config:MessageTimeoutMs"] = "3000",
        });

        builder.Services.AddDbContext<TestOutboxDbContext>(options => TestOutboxDbContext.Configure(options, ConnectionString));
        builder.AddOutboxPublisher<TestOutboxDbContext>();
        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }

    /// <summary>
    /// A host (not started) with the integration event consumer registered as a service would register it, for
    /// <see cref="TestOutboxDbContext"/>, consuming <paramref name="topic"/> in <paramref name="groupId"/> with short retry
    /// delays, and <see cref="RecordingApprovedHandler"/> as the handler of <see cref="AuthorizationApproved"/>, plus
    /// any other <paramref name="settings"/> (e.g. <c>Messaging:Consumer:MaxAttempts</c>).
    /// </summary>
    public IHost CreateConsumerHost(
        string topic,
        string groupId,
        Action<IServiceCollection>? configureServices = null,
        LogCapture? logs = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Production });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{MessagingHostingExtensions.DefaultKafkaConnectionName}"] = BootstrapServers,
            ["Messaging:Consumer:RetryInitialDelay"] = "00:00:00.100",
            ["Messaging:Consumer:RetryMaxDelay"] = "00:00:00.500",
        });
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

        builder.Services.AddDbContext<TestOutboxDbContext>(options => TestOutboxDbContext.Configure(options, ConnectionString));
        if (logs is not null)
        {
            builder.Logging.SetMinimumLevel(LogLevel.Debug).AddProvider(logs);
        }

        builder.AddIntegrationEventConsumer<TestOutboxDbContext>(groupId, [topic]);
        builder.Services.AddSingleton<HandlerProbe>();
        builder.Services.AddIntegrationEventHandler<AuthorizationApproved, RecordingApprovedHandler>();
        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }

    /// <summary>
    /// The services the processor needs, without Kafka: <see cref="TestOutboxDbContext"/>, a
    /// <see cref="RecordingApprovedHandler"/> and the <see cref="IntegrationEventProcessor{TDbContext}"/>.
    /// </summary>
    public ServiceProvider CreateProcessorServices(Action<IServiceCollection>? configureServices = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<TestOutboxDbContext>(options => TestOutboxDbContext.Configure(options, ConnectionString));
        services.AddSingleton<HandlerProbe>();
        services.AddIntegrationEventHandler<AuthorizationApproved, RecordingApprovedHandler>();
        services.AddSingleton<IntegrationEventProcessor<TestOutboxDbContext>>();
        configureServices?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>Sends <paramref name="value"/> to <paramref name="topic"/> with key <paramref name="key"/> and waits for the acknowledgement.</summary>
    public async Task<TopicPartitionOffset> ProduceAsync(string topic, string key, byte[] value, Headers? headers = null)
    {
        using IProducer<string, byte[]> producer =
            new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = BootstrapServers }).Build();
        DeliveryResult<string, byte[]> delivered =
            await producer.ProduceAsync(topic, new Message<string, byte[]> { Key = key, Value = value, Headers = headers });
        return delivered.TopicPartitionOffset;
    }

    /// <summary>Sends <paramref name="value"/> to partition <paramref name="partition"/> of <paramref name="topic"/>.</summary>
    public async Task<TopicPartitionOffset> ProduceAsync(string topic, int partition, string key, byte[] value)
    {
        using IProducer<string, byte[]> producer =
            new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = BootstrapServers }).Build();
        DeliveryResult<string, byte[]> delivered = await producer.ProduceAsync(
            new TopicPartition(topic, partition),
            new Message<string, byte[]> { Key = key, Value = value });
        return delivered.TopicPartitionOffset;
    }

    /// <summary>The offset committed by <paramref name="groupId"/> on each partition of <paramref name="topic"/> (-1001 when none).</summary>
    public Dictionary<int, long> CommittedOffsets(string groupId, string topic)
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
            .ToDictionary(offset => offset.Partition.Value, offset => offset.Offset.Value);
    }

    public async Task<List<ProcessedMessage>> ReadProcessedMessagesAsync()
    {
        await using TestOutboxDbContext dbContext = CreateDbContext();
        return await dbContext.ProcessedMessages.AsNoTracking().ToListAsync();
    }

    /// <summary>Writes one row per card id, in order, each in its own transaction, and returns them as written.</summary>
    public async Task<List<OutboxMessage>> WriteEventsAsync(string topic, params Guid[] cardIds)
    {
        List<OutboxMessage> messages = [];
        foreach (Guid cardId in cardIds)
        {
            await using TestOutboxDbContext dbContext = CreateDbContext();
            OutboxMessage message = OutboxMessage.Create(NewEnvelope(cardId), topic, DateTimeOffset.UtcNow);
            dbContext.OutboxMessages.Add(message);
            await dbContext.SaveChangesAsync();
            messages.Add(message);
        }

        return messages;
    }

    public async Task<List<OutboxMessage>> ReadOutboxAsync()
    {
        await using TestOutboxDbContext dbContext = CreateDbContext();
        return await dbContext.OutboxMessages.AsNoTracking().OrderBy(message => message.Id).ToListAsync();
    }

    /// <summary>Waits until <paramref name="condition"/> holds for the outbox rows, or fails after <paramref name="timeout"/>.</summary>
    public async Task<List<OutboxMessage>> WaitForOutboxAsync(Func<List<OutboxMessage>, bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            List<OutboxMessage> rows = await ReadOutboxAsync();
            if (condition(rows))
            {
                return rows;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"The outbox did not reach the expected state within {timeout}.");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>Reads <paramref name="count"/> messages of <paramref name="topic"/> from the start, or fails after a timeout.</summary>
    public Task<List<ConsumeResult<string, byte[]>>> ConsumeAsync(string topic, int count, TimeSpan? timeout = null)
    {
        return Task.Run(() =>
        {
            using IConsumer<string, byte[]> consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
            {
                BootstrapServers = BootstrapServers,
                GroupId = "test-" + Guid.NewGuid().ToString("N"),
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
            }).Build();
            consumer.Subscribe(topic);

            List<ConsumeResult<string, byte[]>> results = [];
            DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
            while (results.Count < count && DateTime.UtcNow < deadline)
            {
                ConsumeResult<string, byte[]>? result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (result is { IsPartitionEOF: false, Message: not null })
                {
                    results.Add(result);
                }
            }

            consumer.Close();
            Assert.True(results.Count >= count, $"Only {results.Count} of {count} messages arrived on {topic}.");
            return results;
        });
    }

    /// <summary>How many messages <paramref name="topic"/> holds in total (sum of the partitions' high watermarks).</summary>
    public long CountMessages(string topic)
    {
        using IAdminClient admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        Metadata metadata = admin.GetMetadata(topic, TimeSpan.FromSeconds(10));

        using IConsumer<string, byte[]> consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = BootstrapServers,
            GroupId = "test-" + Guid.NewGuid().ToString("N"),
        }).Build();

        return metadata.Topics.Single().Partitions
            .Sum(partition => consumer
                .QueryWatermarkOffsets(new TopicPartition(topic, partition.PartitionId), TimeSpan.FromSeconds(10))
                .High.Value);
    }

    public static EventEnvelope NewEnvelope(Guid cardId)
    {
        AuthorizationApproved approved = new(
            Guid.CreateVersion7(),
            cardId,
            Guid.NewGuid(),
            12.34m,
            "BRL",
            DateTimeOffset.Parse("2026-11-25T10:00:00Z", CultureInfo.InvariantCulture));

        return EventEnvelope.Create(approved, Guid.CreateVersion7(), approved.CreatedAt);
    }
}

[CollectionDefinition(Name)]
public sealed class MessagingCollectionFixture : ICollectionFixture<MessagingFixture>
{
    public const string Name = "Messaging";
}
