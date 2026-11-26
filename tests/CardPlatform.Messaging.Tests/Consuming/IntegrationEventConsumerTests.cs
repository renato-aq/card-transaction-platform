using System.Globalization;

using CardPlatform.Contracts;
using CardPlatform.Messaging.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CardPlatform.Messaging.Tests.Consuming;

/// <summary>The integration event consumer against a real Kafka broker and PostgreSQL (ADR 0011).</summary>
[Collection(MessagingCollectionFixture.Name)]
public sealed class IntegrationEventConsumerTests(MessagingFixture fixture) : IAsyncLifetime
{
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
    public async Task Events_are_processed_and_their_offsets_committed_after_processing()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 3);
        string groupId = NewGroupId();
        EventEnvelope[] envelopes = [.. Enumerable.Range(0, 5).Select(_ => MessagingFixture.NewEnvelope(Guid.NewGuid()))];
        foreach (EventEnvelope envelope in envelopes)
        {
            await fixture.ProduceAsync(topic, envelope.PartitionKey, EventSerializer.SerializeToUtf8Bytes(envelope));
        }

        using IHost host = fixture.CreateConsumerHost(topic, groupId);
        await host.StartAsync();
        await WaitForAsync(async () => (await fixture.ReadProcessedMessagesAsync()).Count == envelopes.Length);
        await host.StopAsync();

        Assert.Equal(envelopes.Select(e => e.EventId).Order(), (await fixture.ReadProcessedMessagesAsync()).Select(m => m.EventId).Order());
        Assert.Equal(envelopes.Length, (await fixture.ReadOutboxAsync()).Count);
        Assert.Equal(envelopes.Length, fixture.CommittedOffsets(groupId, topic).Values.Where(offset => offset > 0).Sum());
    }

    [Fact]
    public async Task A_failing_message_is_retried_until_it_succeeds_and_the_next_ones_of_its_partition_wait_for_it()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        Guid cardId = Guid.NewGuid();
        EventEnvelope failing = MessagingFixture.NewEnvelope(cardId);
        EventEnvelope next = MessagingFixture.NewEnvelope(cardId);
        string key = cardId.ToString("D", CultureInfo.InvariantCulture);
        await fixture.ProduceAsync(topic, key, EventSerializer.SerializeToUtf8Bytes(failing));
        await fixture.ProduceAsync(topic, key, EventSerializer.SerializeToUtf8Bytes(next));

        // Three failures stay within the limit of four attempts, so the message is retried and never dead-lettered.
        using IHost host = fixture.CreateConsumerHost(
            topic,
            groupId,
            settings: new Dictionary<string, string?> { ["Messaging:Consumer:MaxAttempts"] = "4" });
        HandlerProbe probe = host.Services.GetRequiredService<HandlerProbe>();
        int failures = 0;
        probe.OnHandle = envelope =>
            envelope.EventId == failing.EventId && Interlocked.Increment(ref failures) <= 3
                ? throw new InvalidOperationException("Temporary failure.")
                : Task.CompletedTask;

        await host.StartAsync();
        await WaitForAsync(async () => (await fixture.ReadProcessedMessagesAsync()).Count == 2);
        await host.StopAsync();

        Assert.Equal([failing.EventId, failing.EventId, failing.EventId, failing.EventId, next.EventId], probe.Calls);
        Assert.Equal(2, Assert.Single(fixture.CommittedOffsets(groupId, topic)).Value);
        Assert.Equal(0, fixture.CountMessages(Topics.DeadLetterOf(topic)));
    }

    [Fact]
    public async Task A_restarted_consumer_resumes_after_the_committed_offsets()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        EventEnvelope first = MessagingFixture.NewEnvelope(Guid.NewGuid());
        await fixture.ProduceAsync(topic, first.PartitionKey, EventSerializer.SerializeToUtf8Bytes(first));

        using (IHost host = fixture.CreateConsumerHost(topic, groupId))
        {
            await host.StartAsync();
            await WaitForAsync(async () => (await fixture.ReadProcessedMessagesAsync()).Count == 1);
            await host.StopAsync();
        }

        EventEnvelope second = MessagingFixture.NewEnvelope(Guid.NewGuid());
        await fixture.ProduceAsync(topic, second.PartitionKey, EventSerializer.SerializeToUtf8Bytes(second));

        LogCapture logs = new();
        using IHost restarted = fixture.CreateConsumerHost(topic, groupId, logs: logs);
        HandlerProbe probe = restarted.Services.GetRequiredService<HandlerProbe>();
        await restarted.StartAsync();
        await WaitForAsync(async () => (await fixture.ReadProcessedMessagesAsync()).Count == 2);
        await restarted.StopAsync();

        // The first message (offset 0) was committed, so the restarted consumer never received it, not even to skip it.
        Assert.Equal([1L], logs.ConsumedOffsets());
        Assert.Equal([second.EventId], probe.Calls);
        Assert.Equal(2, Assert.Single(fixture.CommittedOffsets(groupId, topic)).Value);
    }

    private static string NewGroupId()
    {
        return "consumer-test-" + Guid.NewGuid().ToString("N");
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
