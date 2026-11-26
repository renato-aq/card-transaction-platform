using System.Globalization;
using System.Text;

using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Contracts.Fraud;
using CardPlatform.Fraud.Application.Signals;
using CardPlatform.Fraud.Domain.Rules;
using CardPlatform.Fraud.Worker.Tests.Infrastructure;
using CardPlatform.Messaging.Kafka;
using CardPlatform.Messaging.Outbox;

using Confluent.Kafka;

namespace CardPlatform.Fraud.Worker.Tests;

/// <summary>
/// The fraud rules end to end (spec 005): approvals consumed from a Kafka topic, judged by the real Fraud Worker host,
/// and the <see cref="FraudSuspected"/> they raise written to its outbox in the processing transaction and published on
/// <c>fraud-signals</c> (key = card id) by the outbox publisher. <c>fraud-signals</c> is shared by the tests (the worker
/// creates it in Development), so each test looks only at the messages of its own card.
/// </summary>
[Collection(WorkerCollectionFixture.Name)]
public sealed class FraudSignalsTests(PostgreSqlFixture postgres, KafkaFixture kafka)
{
    private static readonly DateTimeOffset AuthorizedAt = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rapid_approved_purchases_of_a_card_publish_one_fraud_suspected_with_the_default_limits()
    {
        string topic = await kafka.CreateTopicAsync();
        string groupId = NewGroupId();
        Guid cardId = Guid.NewGuid();

        // Five approved purchases within 20 s: the default velocity limit (5 in 60 s) fires on the fifth.
        AuthorizationApproved[] approvals =
        [
            .. Enumerable.Range(0, 5).Select(index => Approved(cardId, 50m, AuthorizedAt.AddSeconds(index * 5))),
        ];
        foreach (AuthorizationApproved approved in approvals)
        {
            await kafka.PublishAsync(topic, Envelope(approved));
        }

        await using RunningWorker worker = await RunningWorker.StartAsync(postgres, kafka, topic, groupId);
        await WaitUntilProcessedAsync(groupId, topic, approvals.Length);

        OutboxMessage row = Assert.Single(await WaitUntilPublishedAsync(worker, cardId, expected: 1));
        Message<string, byte[]> message = Assert.Single(await SignalsOfAsync(cardId));

        Assert.Equal(Key(cardId), message.Key);
        Assert.Equal(row.EventId.ToString(), Header(message, OutboxHeaders.EventId));
        Assert.Equal(EventTypes.FraudSuspected, Header(message, OutboxHeaders.EventType));

        EventEnvelope envelope = EventSerializer.Deserialize(message.Value);
        Assert.Equal(FraudSignals.EventIdOf(approvals[^1].AuthorizationId), envelope.EventId);
        Assert.Equal(Key(cardId), envelope.PartitionKey);
        FraudSuspected suspected = EventSerializer.DeserializePayload<FraudSuspected>(envelope);
        Assert.Equal(cardId, suspected.CardId);
        Assert.Equal(approvals[^1].AuthorizationId, suspected.AuthorizationId);
        Assert.Equal(VelocityRule.Code, suspected.Rule);
        Assert.Equal("5 approved purchases of the card within 60 s (threshold: 5).", suspected.Reason);
        Assert.Equal(envelope.OccurredAt, suspected.DetectedAt);
        Assert.Equal(Topics.FraudSignals, row.Topic);
    }

    [Fact]
    public async Task A_purchase_far_above_the_card_average_publishes_one_fraud_suspected()
    {
        string topic = await kafka.CreateTopicAsync();
        string groupId = NewGroupId();
        Guid cardId = Guid.NewGuid();

        // Five normal purchases an hour apart (average 100 BRL), then one of 1000 BRL: above 3× the average.
        decimal[] amounts = [80m, 120m, 100m, 90m, 110m];
        AuthorizationApproved[] normal =
        [
            .. amounts.Select((amount, index) => Approved(cardId, amount, AuthorizedAt.AddHours(index))),
        ];
        AuthorizationApproved large = Approved(cardId, 1_000m, AuthorizedAt.AddHours(5));
        foreach (AuthorizationApproved approved in normal.Append(large))
        {
            await kafka.PublishAsync(topic, Envelope(approved));
        }

        await using RunningWorker worker = await RunningWorker.StartAsync(postgres, kafka, topic, groupId);
        await WaitUntilProcessedAsync(groupId, topic, normal.Length + 1);

        Assert.Single(await WaitUntilPublishedAsync(worker, cardId, expected: 1));
        FraudSuspected suspected = Assert.Single(Payloads(await SignalsOfAsync(cardId)));

        Assert.Equal(large.AuthorizationId, suspected.AuthorizationId);
        Assert.Equal(AmountAboveAverageRule.Code, suspected.Rule);
        Assert.Equal(
            "Amount 1000.00 BRL is above 3x the average of 100.00 BRL over 5 previous approved purchases in BRL.",
            suspected.Reason);
    }

    [Fact]
    public async Task Normal_purchases_publish_no_fraud_suspected()
    {
        string topic = await kafka.CreateTopicAsync();
        string groupId = NewGroupId();
        Guid cardId = Guid.NewGuid();

        // Spaced out and within 3× the average; a declined purchase and one in another currency do not count either.
        decimal[] amounts = [80m, 120m, 100m, 90m, 110m, 290m];
        IAuthorizationEvent[] events =
        [
            .. amounts.Select((amount, index) => Approved(cardId, amount, AuthorizedAt.AddHours(index))),
            new AuthorizationDeclined(
                Guid.CreateVersion7(), cardId, Guid.NewGuid(), 5_000m, "BRL", AuthorizedAt.AddHours(6), DeclineReasons.InsufficientFunds),
            new AuthorizationApproved(Guid.CreateVersion7(), cardId, Guid.NewGuid(), 900m, "USD", AuthorizedAt.AddHours(7)),
        ];
        foreach (IAuthorizationEvent integrationEvent in events)
        {
            await kafka.PublishAsync(topic, Envelope(integrationEvent));
        }

        await using RunningWorker worker = await RunningWorker.StartAsync(postgres, kafka, topic, groupId);
        await WaitUntilProcessedAsync(groupId, topic, events.Length);

        // Every message is committed, so nothing more can be written; the outbox is all that is ever published.
        Assert.Equal(events.Length, (await worker.ProcessedMessagesAsync()).Count);
        Assert.Empty(await worker.OutboxMessagesAsync());
        Assert.Empty(await SignalsOfAsync(cardId));
    }

    [Fact]
    public async Task A_duplicated_triggering_message_publishes_a_single_fraud_suspected()
    {
        string topic = await kafka.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        Guid cardId = Guid.NewGuid();

        // Limits from configuration: two approved purchases within 60 s fire the velocity rule.
        string[] settings = ["--Fraud:Rules:Velocity:Threshold=2", "--Fraud:Rules:Velocity:Window=00:01:00"];
        AuthorizationApproved first = Approved(cardId, 50m, AuthorizedAt);
        AuthorizationApproved triggering = Approved(cardId, 50m, AuthorizedAt.AddSeconds(10));
        EventEnvelope triggeringEnvelope = Envelope(triggering);

        await kafka.PublishAsync(topic, Envelope(first));
        // At least once: the same message again (same event id), and the same approval in another envelope.
        await kafka.PublishAsync(topic, triggeringEnvelope);
        await kafka.PublishAsync(topic, triggeringEnvelope);
        await kafka.PublishAsync(topic, Envelope(triggering));

        await using RunningWorker worker = await RunningWorker.StartAsync(postgres, kafka, topic, groupId, settings);
        await WaitUntilProcessedAsync(groupId, topic, messages: 4);

        Assert.Equal(4, worker.Logs.ConsumedOffsets().Length);
        Assert.Equal(3, (await worker.ProcessedMessagesAsync()).Count);
        OutboxMessage row = Assert.Single(await WaitUntilPublishedAsync(worker, cardId, expected: 1));
        Assert.Equal(FraudSignals.EventIdOf(triggering.AuthorizationId), row.EventId);

        FraudSuspected suspected = Assert.Single(Payloads(await SignalsOfAsync(cardId)));
        Assert.Equal(triggering.AuthorizationId, suspected.AuthorizationId);
        Assert.Equal("2 approved purchases of the card within 60 s (threshold: 2).", suspected.Reason);
    }

    private static AuthorizationApproved Approved(Guid cardId, decimal amount, DateTimeOffset authorizedAt)
    {
        return new AuthorizationApproved(Guid.CreateVersion7(), cardId, Guid.NewGuid(), amount, "BRL", authorizedAt);
    }

    private static EventEnvelope Envelope(IAuthorizationEvent integrationEvent)
    {
        return EventEnvelope.Create(integrationEvent, Guid.CreateVersion7(), integrationEvent.CreatedAt);
    }

    private static string NewGroupId()
    {
        return "fraud-worker-test-" + Guid.NewGuid().ToString("N");
    }

    private static string Key(Guid cardId)
    {
        return cardId.ToString("D", CultureInfo.InvariantCulture);
    }

    private static string? Header(Message<string, byte[]> message, string name)
    {
        return message.Headers.TryGetLastBytes(name, out byte[] value) ? Encoding.UTF8.GetString(value) : null;
    }

    private static IEnumerable<FraudSuspected> Payloads(IEnumerable<Message<string, byte[]>> messages)
    {
        return messages.Select(static message =>
            EventSerializer.DeserializePayload<FraudSuspected>(EventSerializer.Deserialize(message.Value)));
    }

    /// <summary>Waits until the worker committed the offsets of all <paramref name="messages"/>: they are all processed.</summary>
    private Task WaitUntilProcessedAsync(string groupId, string topic, int messages)
    {
        return RunningWorker.WaitForAsync(() => Task.FromResult(kafka.CommittedCount(groupId, topic) == messages));
    }

    /// <summary>Waits until the card has <paramref name="expected"/> outbox rows, all acknowledged by the broker.</summary>
    private static async Task<List<OutboxMessage>> WaitUntilPublishedAsync(RunningWorker worker, Guid cardId, int expected)
    {
        List<OutboxMessage> rows = [];
        await RunningWorker.WaitForAsync(async () =>
        {
            rows = [.. (await worker.OutboxMessagesAsync()).Where(row => row.PartitionKey == Key(cardId))];
            return rows.Count >= expected && rows.All(static row => row.PublishedAt is not null);
        });

        return rows;
    }

    /// <summary>The messages of the card on <c>fraud-signals</c>, read to the end of every partition.</summary>
    private async Task<List<Message<string, byte[]>>> SignalsOfAsync(Guid cardId)
    {
        return [.. (await kafka.ReadAllAsync(Topics.FraudSignals)).Where(message => message.Key == Key(cardId))];
    }
}
