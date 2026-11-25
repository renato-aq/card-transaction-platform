using System.Globalization;
using System.Text;

using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Messaging.Kafka;
using CardPlatform.Messaging.Outbox;
using CardPlatform.Messaging.Publishing;
using CardPlatform.Messaging.Tests.Infrastructure;

using Confluent.Kafka;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CardPlatform.Messaging.Tests.Publishing;

/// <summary>
/// The outbox publisher against a real PostgreSQL and a real Kafka broker (spec 002 of the outbox task, ADR 0010).
/// </summary>
[Collection(MessagingCollectionFixture.Name)]
public sealed class OutboxPublisherTests(MessagingFixture fixture) : IAsyncLifetime
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
    public async Task Event_written_to_the_outbox_reaches_its_topic_keyed_by_card_id_and_is_marked_published()
    {
        string topic = await fixture.CreateTopicAsync();
        Guid cardId = Guid.NewGuid();
        OutboxMessage written = Assert.Single(await fixture.WriteEventsAsync(topic, cardId));

        using IHost host = fixture.CreatePublisherHost();
        await host.StartAsync();
        OutboxMessage row = Assert.Single(
            await fixture.WaitForOutboxAsync(rows => rows.All(r => r.PublishedAt is not null), Timeout));
        await host.StopAsync();

        ConsumeResult<string, byte[]> result = Assert.Single(await fixture.ConsumeAsync(topic, 1));
        Assert.Equal(cardId.ToString("D", CultureInfo.InvariantCulture), result.Message.Key);

        EventEnvelope envelope = EventSerializer.Deserialize(result.Message.Value);
        EventEnvelope expected = written.ReadEnvelope();
        Assert.Equal(expected.EventId, envelope.EventId);
        Assert.Equal(EventTypes.AuthorizationApproved, envelope.Type);
        Assert.Equal(expected.PartitionKey, envelope.PartitionKey);
        Assert.Equal(expected.OccurredAt, envelope.OccurredAt);
        Assert.Equal(
            EventSerializer.DeserializePayload<AuthorizationApproved>(expected),
            EventSerializer.DeserializePayload<AuthorizationApproved>(envelope));

        Assert.Equal(expected.EventId.ToString("D", CultureInfo.InvariantCulture), Header(result, OutboxHeaders.EventId));
        Assert.Equal(EventTypes.AuthorizationApproved, Header(result, OutboxHeaders.EventType));

        Assert.NotNull(row.PublishedAt);
        Assert.Equal(1, row.Attempts);
    }

    [Fact]
    public async Task Events_of_the_same_card_arrive_on_one_partition_in_the_order_they_were_written()
    {
        string topic = await fixture.CreateTopicAsync(partitions: 3);
        Guid card = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        Guid[] cards = [.. Enumerable.Range(0, 30).Select(index => index % 3 == 0 ? other : card)];
        List<OutboxMessage> written = await fixture.WriteEventsAsync(topic, cards);

        using IHost host = fixture.CreatePublisherHost();
        await host.StartAsync();
        await fixture.WaitForOutboxAsync(rows => rows.All(r => r.PublishedAt is not null), Timeout);
        await host.StopAsync();

        List<ConsumeResult<string, byte[]>> results = await fixture.ConsumeAsync(topic, written.Count);
        Assert.Equal(written.Count, fixture.CountMessages(topic));

        foreach (Guid cardId in new[] { card, other })
        {
            string key = cardId.ToString("D", CultureInfo.InvariantCulture);
            List<ConsumeResult<string, byte[]>> ofCard = [.. results.Where(result => result.Message.Key == key)];

            Assert.Single(ofCard.Select(result => result.Partition.Value).Distinct());
            Guid[] inOffsetOrder =
            [
                .. ofCard.OrderBy(result => result.Offset.Value)
                    .Select(result => EventSerializer.Deserialize(result.Message.Value).EventId),
            ];
            Guid[] inWriteOrder = [.. written.Where(m => m.PartitionKey == key).OrderBy(m => m.Id).Select(m => m.EventId)];
            Assert.Equal(inWriteOrder, inOffsetOrder);
        }
    }

    [Fact]
    public async Task While_kafka_is_down_events_wait_in_the_outbox_and_all_arrive_once_it_is_back()
    {
        string topic = await fixture.CreateTopicAsync();
        Guid card = Guid.NewGuid();
        using IHost host = fixture.CreatePublisherHost();
        await host.StartAsync();

        List<OutboxMessage> written;
        await fixture.Kafka.PauseAsync();
        try
        {
            written = await fixture.WriteEventsAsync(topic, card, Guid.NewGuid(), card, Guid.NewGuid(), card);

            // Each failed cycle counts an attempt on the first row and stops; it is retried with backoff.
            List<OutboxMessage> waiting = await fixture.WaitForOutboxAsync(rows => rows[0].Attempts >= 2, Timeout);
            Assert.All(waiting, row => Assert.Null(row.PublishedAt));
            Assert.All(waiting.Skip(1), row => Assert.Equal(0, row.Attempts));
        }
        finally
        {
            await fixture.Kafka.UnpauseAsync();
        }

        List<OutboxMessage> published =
            await fixture.WaitForOutboxAsync(rows => rows.All(r => r.PublishedAt is not null), Timeout);
        await host.StopAsync();
        Assert.Equal(written.Count, published.Count);

        // At least once: an event sent while the broker was frozen may also have been stored, so duplicates are allowed,
        // but none is lost and the first copies of each card's events keep the write order.
        List<ConsumeResult<string, byte[]>> results = await fixture.ConsumeAsync(topic, written.Count);
        Guid[] received = [.. results.Select(result => EventSerializer.Deserialize(result.Message.Value).EventId)];
        Assert.Equal(written.Select(m => m.EventId).Order(), received.Distinct().Order());

        string key = card.ToString("D", CultureInfo.InvariantCulture);
        Guid[] firstCopiesInOffsetOrder =
        [
            .. results.Where(result => result.Message.Key == key)
                .OrderBy(result => result.Offset.Value)
                .Select(result => EventSerializer.Deserialize(result.Message.Value).EventId)
                .Distinct(),
        ];
        Assert.Equal(written.Where(m => m.PartitionKey == key).Select(m => m.EventId), firstCopiesInOffsetOrder);
    }

    [Fact]
    public async Task A_second_instance_skips_the_cycle_while_another_one_is_publishing()
    {
        string topic = await fixture.CreateTopicAsync();
        List<OutboxMessage> written = await fixture.WriteEventsAsync(
            topic, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        GatedProducer gate = new();
        using IHost first = fixture.CreatePublisherHost(services => services.Replace(
            ServiceDescriptor.Singleton<IOutboxMessageProducer>(serviceProvider =>
                gate.Wrap(new KafkaOutboxMessageProducer(serviceProvider.GetRequiredService<IProducer<string, byte[]>>())))));
        using IHost second = fixture.CreatePublisherHost();
        OutboxBatchPublisher<TestOutboxDbContext> firstPublisher =
            first.Services.GetRequiredService<OutboxBatchPublisher<TestOutboxDbContext>>();
        OutboxBatchPublisher<TestOutboxDbContext> secondPublisher =
            second.Services.GetRequiredService<OutboxBatchPublisher<TestOutboxDbContext>>();

        Task<OutboxBatchResult> firstCycle = firstPublisher.PublishPendingAsync(CancellationToken.None);
        await gate.Entered.WaitAsync(Timeout);

        // The first instance holds the lock and is in the middle of its batch.
        OutboxBatchResult skipped = await secondPublisher.PublishPendingAsync(CancellationToken.None);
        Assert.True(skipped.Skipped);
        Assert.Equal(0, skipped.Published);

        gate.Open();
        OutboxBatchResult firstResult = await firstCycle.WaitAsync(Timeout);
        Assert.False(firstResult.Skipped);
        Assert.Equal(written.Count, firstResult.Published);
        Assert.Null(firstResult.Failure);

        OutboxBatchResult nothingLeft = await secondPublisher.PublishPendingAsync(CancellationToken.None);
        Assert.False(nothingLeft.Skipped);
        Assert.Equal(0, nothingLeft.Published);

        Assert.Equal(written.Count, fixture.CountMessages(topic));
        Assert.All(await fixture.ReadOutboxAsync(), row =>
        {
            Assert.NotNull(row.PublishedAt);
            Assert.Equal(1, row.Attempts);
        });
    }

    [Fact]
    public async Task A_failed_send_stops_the_cycle_and_counts_the_attempt_so_later_events_do_not_overtake_it()
    {
        List<OutboxMessage> written = await fixture.WriteEventsAsync(
            "unused-topic", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        FailingOnceProducer producer = new(failingMessageId: written[1].Id);
        using IHost host = fixture.CreatePublisherHost(services =>
            services.Replace(ServiceDescriptor.Singleton<IOutboxMessageProducer>(producer)));
        OutboxBatchPublisher<TestOutboxDbContext> publisher =
            host.Services.GetRequiredService<OutboxBatchPublisher<TestOutboxDbContext>>();

        OutboxBatchResult failed = await publisher.PublishPendingAsync(CancellationToken.None);

        Assert.Equal(1, failed.Published);
        Assert.IsType<InvalidOperationException>(failed.Failure);
        Assert.False(failed.MayHaveMore);
        List<OutboxMessage> rows = await fixture.ReadOutboxAsync();
        Assert.NotNull(rows[0].PublishedAt);
        Assert.Equal(1, rows[0].Attempts);
        Assert.Null(rows[1].PublishedAt);
        Assert.Equal(1, rows[1].Attempts);
        Assert.Null(rows[2].PublishedAt);
        Assert.Equal(0, rows[2].Attempts);

        OutboxBatchResult retried = await publisher.PublishPendingAsync(CancellationToken.None);

        Assert.Equal(2, retried.Published);
        Assert.Null(retried.Failure);
        Assert.Equal([written[0].Id, written[1].Id, written[1].Id, written[2].Id], producer.Sent);
        rows = await fixture.ReadOutboxAsync();
        Assert.All(rows, row => Assert.NotNull(row.PublishedAt));
        Assert.Equal([1, 2, 1], rows.Select(row => row.Attempts));
    }

    private static string Header(ConsumeResult<string, byte[]> result, string name)
    {
        return Encoding.UTF8.GetString(result.Message.Headers.GetLastBytes(name));
    }

    /// <summary>Holds every send until <see cref="Open"/> is called, and signals when the first one arrives.</summary>
    private sealed class GatedProducer
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Open()
        {
            _open.TrySetResult();
        }

        public IOutboxMessageProducer Wrap(IOutboxMessageProducer inner)
        {
            return new Gate(this, inner);
        }

        private sealed class Gate(GatedProducer owner, IOutboxMessageProducer inner) : IOutboxMessageProducer
        {
            public async Task ProduceAsync(OutboxMessage message, CancellationToken cancellationToken)
            {
                owner._entered.TrySetResult();
                await owner._open.Task.WaitAsync(cancellationToken);
                await inner.ProduceAsync(message, cancellationToken);
            }
        }
    }

    /// <summary>Records what is sent without a broker, failing the first send of one row.</summary>
    private sealed class FailingOnceProducer(long failingMessageId) : IOutboxMessageProducer
    {
        private bool _failed;

        public List<long> Sent { get; } = [];

        public Task ProduceAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message.Id);
            if (message.Id == failingMessageId && !_failed)
            {
                _failed = true;
                throw new InvalidOperationException("Broker unavailable.");
            }

            return Task.CompletedTask;
        }
    }
}
