using System.Globalization;
using System.Text;

using CardPlatform.Contracts;
using CardPlatform.Messaging.Consuming;
using CardPlatform.Messaging.Kafka;
using CardPlatform.Messaging.Tests.Infrastructure;

using Confluent.Kafka;

using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardPlatform.Messaging.Tests.Consuming;

/// <summary>
/// Retries and the dead-letter topic of the integration event consumer, against a real Kafka broker and PostgreSQL
/// (spec 007, ADR 0011): permanent failures and messages that keep failing go to <c>&lt;topic&gt;.dlt</c> with error
/// headers, their offset is committed and the next messages of the partition are processed.
/// </summary>
[Collection(MessagingCollectionFixture.Name)]
public sealed class DeadLetterTests(MessagingFixture fixture) : IAsyncLifetime
{
    private const int DeadLetteredLogId = 120;
    private const int DeadLetterFailedLogId = 121;
    private const int ProcessingFailedLogId = 112;
    private const int DeadLetterSkippedAtShutdownLogId = 119;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public Task InitializeAsync()
    {
        return fixture.ResetOutboxAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_invalid_message_goes_to_the_dead_letter_topic_with_error_headers_and_the_next_messages_of_its_partition_are_processed()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        Guid cardId = Guid.NewGuid();
        string key = cardId.ToString("D", CultureInfo.InvariantCulture);
        byte[] invalid = Encoding.UTF8.GetBytes("{ this is not an envelope");
        EventEnvelope next = MessagingFixture.NewEnvelope(cardId);
        EventEnvelope last = MessagingFixture.NewEnvelope(cardId);
        Headers originalHeaders = new() { { "trace-id", Encoding.UTF8.GetBytes("abc-123") } };
        TopicPartitionOffset invalidAt = await fixture.ProduceAsync(topic, key, invalid, originalHeaders);
        await fixture.ProduceAsync(topic, key, EventSerializer.SerializeToUtf8Bytes(next));
        await fixture.ProduceAsync(topic, key, EventSerializer.SerializeToUtf8Bytes(last));

        LogCapture logs = new();
        using IHost host = fixture.CreateConsumerHost(topic, groupId, logs: logs);
        HandlerProbe probe = host.Services.GetRequiredService<HandlerProbe>();
        await host.StartAsync();
        await WaitForAsync(() => Task.FromResult(CommittedOffset(groupId, topic) == 3));
        await host.StopAsync();

        // The next messages of the partition were processed, in order, and every offset was committed.
        Assert.Equal([next.EventId, last.EventId], probe.Calls);
        Assert.Equal(2, (await fixture.ReadProcessedMessagesAsync()).Count);

        ConsumeResult<string, byte[]> deadLetter = Assert.Single(await fixture.ConsumeAsync(Topics.DeadLetterOf(topic), 1));
        Assert.Equal(key, deadLetter.Message.Key);
        Assert.Equal(invalid, deadLetter.Message.Value);
        Assert.Equal("abc-123", Header(deadLetter, "trace-id"));
        Assert.Equal("System.Text.Json.JsonException", Header(deadLetter, DeadLetterHeaders.ExceptionType));
        Assert.False(string.IsNullOrWhiteSpace(Header(deadLetter, DeadLetterHeaders.Reason)));
        Assert.Equal(DeadLetterHeaders.PermanentFailure, Header(deadLetter, DeadLetterHeaders.FailureKind));
        Assert.Equal("1", Header(deadLetter, DeadLetterHeaders.Attempts));
        Assert.Equal(topic, Header(deadLetter, DeadLetterHeaders.OriginalTopic));
        Assert.Equal("0", Header(deadLetter, DeadLetterHeaders.OriginalPartition));
        Assert.Equal(invalidAt.Offset.Value.ToString(CultureInfo.InvariantCulture), Header(deadLetter, DeadLetterHeaders.OriginalOffset));
        Assert.Equal(groupId, Header(deadLetter, DeadLetterHeaders.ConsumerGroup));
        DateTimeOffset failedAt = DateTimeOffset.Parse(Header(deadLetter, DeadLetterHeaders.FailedAt), CultureInfo.InvariantCulture);
        Assert.True(DateTimeOffset.UtcNow - failedAt < Timeout);

        // One structured log per dead-lettered message, and no retry of a permanent failure.
        LogCapture.LogEntry logged = Assert.Single(logs.Entries, static entry => entry.EventId == DeadLetteredLogId);
        Assert.Equal(LogLevel.Error, logged.Level);
        Assert.Equal(topic, logged.Values["Topic"]);
        Assert.Equal(0, logged.Values["Partition"]);
        Assert.Equal(invalidAt.Offset.Value, logged.Values["Offset"]);
        Assert.Equal(1, logged.Values["Attempts"]);
        Assert.Equal(Topics.DeadLetterOf(topic), logged.Values["DeadLetterTopic"]);
        Assert.Null(logged.Values["EventId"]);
        Assert.DoesNotContain(logs.Entries, static entry => entry.EventId == ProcessingFailedLogId);
    }

    [Fact]
    public async Task A_payload_that_does_not_match_its_type_goes_to_the_dead_letter_topic_with_its_event_id_logged()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        EventEnvelope valid = MessagingFixture.NewEnvelope(Guid.NewGuid());
        EventEnvelope unknownShape = valid with
        {
            EventId = Guid.CreateVersion7(),
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { unexpected = true }),
        };
        await fixture.ProduceAsync(topic, unknownShape.PartitionKey, EventSerializer.SerializeToUtf8Bytes(unknownShape));
        await fixture.ProduceAsync(topic, valid.PartitionKey, EventSerializer.SerializeToUtf8Bytes(valid));

        LogCapture logs = new();
        using IHost host = fixture.CreateConsumerHost(topic, groupId, logs: logs);
        await host.StartAsync();
        await WaitForAsync(() => Task.FromResult(CommittedOffset(groupId, topic) == 2));
        await host.StopAsync();

        Assert.Equal([valid.EventId], (await fixture.ReadProcessedMessagesAsync()).Select(static m => m.EventId));
        ConsumeResult<string, byte[]> deadLetter = Assert.Single(await fixture.ConsumeAsync(Topics.DeadLetterOf(topic), 1));
        Assert.Equal(DeadLetterHeaders.PermanentFailure, Header(deadLetter, DeadLetterHeaders.FailureKind));
        Assert.Equal("1", Header(deadLetter, DeadLetterHeaders.Attempts));

        LogCapture.LogEntry logged = Assert.Single(logs.Entries, static entry => entry.EventId == DeadLetteredLogId);
        Assert.Equal(unknownShape.EventId, logged.Values["EventId"]);
        Assert.Equal(EventTypes.AuthorizationApproved, logged.Values["EventType"]);
    }

    [Fact]
    public async Task A_temporary_failure_that_passes_within_the_limit_is_processed_without_going_to_the_dead_letter_topic()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        EventEnvelope flaky = MessagingFixture.NewEnvelope(Guid.NewGuid());
        await fixture.ProduceAsync(topic, flaky.PartitionKey, EventSerializer.SerializeToUtf8Bytes(flaky));

        LogCapture logs = new();
        using IHost host = fixture.CreateConsumerHost(topic, groupId, logs: logs, settings: MaxAttempts(3));
        HandlerProbe probe = host.Services.GetRequiredService<HandlerProbe>();
        int failures = 0;
        probe.OnHandle = _ => Interlocked.Increment(ref failures) <= 2
            ? throw new InvalidOperationException("Database unavailable.")
            : Task.CompletedTask;

        await host.StartAsync();
        await WaitForAsync(() => Task.FromResult(CommittedOffset(groupId, topic) == 1));
        await host.StopAsync();

        // Two failures, then the third and last allowed attempt succeeded.
        Assert.Equal([flaky.EventId, flaky.EventId, flaky.EventId], probe.Calls);
        Assert.Single(await fixture.ReadProcessedMessagesAsync());
        Assert.Equal(0, fixture.CountMessages(Topics.DeadLetterOf(topic)));
        Assert.Equal(
            [1, 2],
            logs.Entries.Where(static entry => entry.EventId == ProcessingFailedLogId).Select(static entry => (int)entry.Values["Attempt"]!));
        Assert.DoesNotContain(logs.Entries, static entry => entry.EventId == DeadLetteredLogId);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_goes_to_the_dead_letter_topic_after_the_maximum_attempts()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        Guid cardId = Guid.NewGuid();
        EventEnvelope failing = MessagingFixture.NewEnvelope(cardId);
        EventEnvelope next = MessagingFixture.NewEnvelope(cardId);
        await fixture.ProduceAsync(topic, failing.PartitionKey, EventSerializer.SerializeToUtf8Bytes(failing));
        await fixture.ProduceAsync(topic, next.PartitionKey, EventSerializer.SerializeToUtf8Bytes(next));

        LogCapture logs = new();
        using IHost host = fixture.CreateConsumerHost(topic, groupId, logs: logs, settings: MaxAttempts(3));
        HandlerProbe probe = host.Services.GetRequiredService<HandlerProbe>();
        probe.OnHandle = envelope => envelope.EventId == failing.EventId
            ? throw new InvalidOperationException("Database unavailable.")
            : Task.CompletedTask;

        await host.StartAsync();
        await WaitForAsync(() => Task.FromResult(CommittedOffset(groupId, topic) == 2));
        await host.StopAsync();

        // Tried three times, before the next message of the partition, then dead-lettered; the next one was processed.
        Assert.Equal([failing.EventId, failing.EventId, failing.EventId, next.EventId], probe.Calls);
        Assert.Equal([next.EventId], (await fixture.ReadProcessedMessagesAsync()).Select(static m => m.EventId));
        Assert.Single(await fixture.ReadOutboxAsync());

        ConsumeResult<string, byte[]> deadLetter = Assert.Single(await fixture.ConsumeAsync(Topics.DeadLetterOf(topic), 1));
        Assert.Equal(EventSerializer.SerializeToUtf8Bytes(failing), deadLetter.Message.Value);
        Assert.Equal("3", Header(deadLetter, DeadLetterHeaders.Attempts));
        Assert.Equal(DeadLetterHeaders.RetriesExhausted, Header(deadLetter, DeadLetterHeaders.FailureKind));
        Assert.Equal(typeof(InvalidOperationException).FullName, Header(deadLetter, DeadLetterHeaders.ExceptionType));
        Assert.Equal("Database unavailable.", Header(deadLetter, DeadLetterHeaders.Reason));

        LogCapture.LogEntry logged = Assert.Single(logs.Entries, static entry => entry.EventId == DeadLetteredLogId);
        Assert.Equal(failing.EventId, logged.Values["EventId"]);
        Assert.Equal(3, logged.Values["Attempts"]);
        Assert.Equal("Database unavailable.", logged.Values["Reason"]);
    }

    [Fact]
    public async Task A_wrapped_failure_is_dead_lettered_with_the_type_and_message_of_its_root_cause()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        EventEnvelope failing = MessagingFixture.NewEnvelope(Guid.NewGuid());
        await fixture.ProduceAsync(topic, failing.PartitionKey, EventSerializer.SerializeToUtf8Bytes(failing));

        LogCapture logs = new();
        using IHost host = fixture.CreateConsumerHost(topic, groupId, logs: logs, settings: MaxAttempts(1));
        HandlerProbe probe = host.Services.GetRequiredService<HandlerProbe>();

        // What EF's retrying execution strategy throws when the database stays down.
        probe.OnHandle = static _ => throw new RetryLimitExceededException(
            "Maximum number of retries (6) exceeded while executing database operations. See the inner exception.",
            new TimeoutException("Database unavailable."));

        await host.StartAsync();
        await WaitForAsync(() => Task.FromResult(CommittedOffset(groupId, topic) == 1));
        await host.StopAsync();

        ConsumeResult<string, byte[]> deadLetter = Assert.Single(await fixture.ConsumeAsync(Topics.DeadLetterOf(topic), 1));
        Assert.Equal(typeof(TimeoutException).FullName, Header(deadLetter, DeadLetterHeaders.ExceptionType));
        Assert.Equal("Database unavailable.", Header(deadLetter, DeadLetterHeaders.Reason));

        LogCapture.LogEntry logged = Assert.Single(logs.Entries, static entry => entry.EventId == DeadLetteredLogId);
        Assert.Equal("Database unavailable.", logged.Values["Reason"]);
    }

    [Fact]
    public async Task A_rejected_event_goes_to_the_dead_letter_topic_after_its_first_attempt()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        EventEnvelope rejected = MessagingFixture.NewEnvelope(Guid.NewGuid());
        await fixture.ProduceAsync(topic, rejected.PartitionKey, EventSerializer.SerializeToUtf8Bytes(rejected));

        using IHost host = fixture.CreateConsumerHost(topic, groupId);
        HandlerProbe probe = host.Services.GetRequiredService<HandlerProbe>();
        probe.OnHandle = static _ => throw new IntegrationEventRejectedException("Invalid currency.");

        await host.StartAsync();
        await WaitForAsync(() => Task.FromResult(CommittedOffset(groupId, topic) == 1));
        await host.StopAsync();

        Assert.Equal([rejected.EventId], probe.Calls);
        Assert.Empty(await fixture.ReadProcessedMessagesAsync());
        ConsumeResult<string, byte[]> deadLetter = Assert.Single(await fixture.ConsumeAsync(Topics.DeadLetterOf(topic), 1));
        Assert.Equal("1", Header(deadLetter, DeadLetterHeaders.Attempts));
        Assert.Equal(DeadLetterHeaders.PermanentFailure, Header(deadLetter, DeadLetterHeaders.FailureKind));
        Assert.Equal(typeof(IntegrationEventRejectedException).FullName, Header(deadLetter, DeadLetterHeaders.ExceptionType));
        Assert.Equal("Invalid currency.", Header(deadLetter, DeadLetterHeaders.Reason));
    }

    [Fact]
    public async Task Attempts_are_counted_per_message_while_other_partitions_keep_being_processed()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 2);
        string groupId = NewGroupId();
        EventEnvelope failing = MessagingFixture.NewEnvelope(Guid.NewGuid());
        EventEnvelope[] others = [.. Enumerable.Range(0, 6).Select(_ => MessagingFixture.NewEnvelope(Guid.NewGuid()))];
        await fixture.ProduceAsync(topic, 0, failing.PartitionKey, EventSerializer.SerializeToUtf8Bytes(failing));
        foreach (EventEnvelope other in others)
        {
            await fixture.ProduceAsync(topic, 1, other.PartitionKey, EventSerializer.SerializeToUtf8Bytes(other));
        }

        LogCapture logs = new();
        using IHost host = fixture.CreateConsumerHost(topic, groupId, logs: logs, settings: MaxAttempts(4));
        HandlerProbe probe = host.Services.GetRequiredService<HandlerProbe>();
        probe.OnHandle = envelope => envelope.EventId == failing.EventId
            ? throw new InvalidOperationException("Database unavailable.")
            : Task.CompletedTask;

        await host.StartAsync();
        await WaitForAsync(() => Task.FromResult(fixture.CommittedOffsets(groupId, topic) is { } offsets
            && offsets[0] == 1
            && offsets[1] == others.Length));
        await host.StopAsync();

        Assert.Equal(4, probe.Calls.Count(id => id == failing.EventId));
        Assert.Equal(others.Length, (await fixture.ReadProcessedMessagesAsync()).Count);
        Assert.Equal(
            [1, 2, 3],
            logs.Entries.Where(static entry => entry.EventId == ProcessingFailedLogId).Select(static entry => (int)entry.Values["Attempt"]!));
        ConsumeResult<string, byte[]> deadLetter = Assert.Single(await fixture.ConsumeAsync(Topics.DeadLetterOf(topic), 1));
        Assert.Equal("4", Header(deadLetter, DeadLetterHeaders.Attempts));
    }

    [Fact]
    public async Task When_the_dead_letter_topic_cannot_be_written_the_message_stays_uncommitted_until_it_can()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        byte[] invalid = Encoding.UTF8.GetBytes("not json");
        await fixture.ProduceAsync(topic, "card", invalid);

        // A producer that cannot reach any broker: every send to the dead-letter topic times out.
        {
            LogCapture logs = new();
            using IHost host = fixture.CreateConsumerHost(
                topic,
                groupId,
                services => services.Replace(ServiceDescriptor.Singleton(serviceProvider => new KafkaProducerProvider(
                    static () => new ProducerBuilder<string, byte[]>(new ProducerConfig
                    {
                        BootstrapServers = "127.0.0.1:1",
                        MessageTimeoutMs = 300,
                    }).Build(),
                    serviceProvider.GetRequiredService<ILogger<KafkaProducerProvider>>()))),
                logs);
            await host.StartAsync();
            await WaitForAsync(() => Task.FromResult(logs.Entries.Count(static entry => entry.EventId == DeadLetterFailedLogId) >= 2));
            await host.StopAsync();

            Assert.Equal(Offset.Unset.Value, CommittedOffset(groupId, topic));
            Assert.Equal(0, fixture.CountMessages(Topics.DeadLetterOf(topic)));
            Assert.DoesNotContain(logs.Entries, static entry => entry.EventId == DeadLetteredLogId);
        }

        // With the broker reachable again, the message is dead-lettered and only then committed.
        using IHost restarted = fixture.CreateConsumerHost(topic, groupId);
        await restarted.StartAsync();
        await WaitForAsync(() => Task.FromResult(CommittedOffset(groupId, topic) == 1));
        await restarted.StopAsync();

        ConsumeResult<string, byte[]> deadLetter = Assert.Single(await fixture.ConsumeAsync(Topics.DeadLetterOf(topic), 1));
        Assert.Equal(invalid, deadLetter.Message.Value);
    }

    [Fact]
    public async Task A_dead_letter_send_after_the_producer_was_disposed_at_shutdown_stops_the_consumer_without_committing()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        EventEnvelope rejected = MessagingFixture.NewEnvelope(Guid.NewGuid());
        await fixture.ProduceAsync(topic, rejected.PartitionKey, EventSerializer.SerializeToUtf8Bytes(rejected));

        LogCapture logs = new();
        using IHost host = fixture.CreateConsumerHost(topic, groupId, logs: logs);
        HandlerProbe probe = host.Services.GetRequiredService<HandlerProbe>();
        TaskCompletionSource handling = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.OnHandle = async _ =>
        {
            handling.SetResult();
            await release.Task;
            throw new IntegrationEventRejectedException("Invalid currency.");
        };

        await host.StartAsync();
        await handling.Task.WaitAsync(Timeout);

        // The host stops while the message is being processed, and its shutdown timeout expires: the container disposes
        // the shared producer before the handler fails and the consumer tries to dead-letter the message.
        Task stopping = host.StopAsync();
        host.Services.GetRequiredService<KafkaProducerProvider>().Dispose();
        release.SetResult();
        await stopping.WaitAsync(Timeout);

        IntegrationEventConsumer<TestOutboxDbContext> consumer =
            host.Services.GetServices<IHostedService>().OfType<IntegrationEventConsumer<TestOutboxDbContext>>().Single();
        Assert.True(consumer.ExecuteTask!.IsCompletedSuccessfully);
        Assert.DoesNotContain(logs.Entries, static entry => entry.Level >= LogLevel.Error);
        Assert.Single(logs.Entries, static entry => entry.EventId == DeadLetterSkippedAtShutdownLogId);
        Assert.Equal(Offset.Unset.Value, CommittedOffset(groupId, topic));
        Assert.Equal(0, fixture.CountMessages(Topics.DeadLetterOf(topic)));
    }

    private static Dictionary<string, string?> MaxAttempts(int value)
    {
        return new Dictionary<string, string?>
        {
            ["Messaging:Consumer:MaxAttempts"] = value.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static string Header(ConsumeResult<string, byte[]> result, string key)
    {
        return Encoding.UTF8.GetString(result.Message.Headers.GetLastBytes(key));
    }

    private long CommittedOffset(string groupId, string topic)
    {
        return Assert.Single(fixture.CommittedOffsets(groupId, topic)).Value;
    }

    private static string NewGroupId()
    {
        return "dead-letter-test-" + Guid.NewGuid().ToString("N");
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"The condition was not met within {Timeout}.");
            }

            await Task.Delay(100);
        }
    }
}
