
using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Fraud.Worker.Tests.Infrastructure;
using CardPlatform.Messaging.Consuming;

namespace CardPlatform.Fraud.Worker.Tests;

/// <summary>
/// The real Fraud Worker host consuming authorization events from a Kafka container into its PostgreSQL database
/// (spec 004, ADR 0011). Events are published as the Authorization's outbox publisher publishes them: key = card id,
/// value = the envelope. Each test consumes its own topic, in its own consumer group, into its own database.
/// </summary>
[Collection(WorkerCollectionFixture.Name)]
public sealed class AuthorizationEventsConsumerTests(PostgreSqlFixture postgres, KafkaFixture kafka)
{
    private static readonly DateTimeOffset AuthorizedAt = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_published_authorization_approved_appears_in_the_card_history()
    {
        string topic = await kafka.CreateTopicAsync();
        Guid cardId = Guid.NewGuid();
        AuthorizationApproved approved = Approved(cardId, 42.5m, AuthorizedAt.AddTicks(1234567));
        EventEnvelope envelope = Envelope(approved);
        await kafka.PublishAsync(topic, envelope);

        await using RunningWorker worker = await RunningWorker.StartAsync(postgres, kafka, topic, NewGroupId());
        CardHistory history = await worker.WaitForHistoryAsync(cardId, static h => h.Purchases.Count == 1);

        CardPurchase purchase = Assert.Single(history.Approved);
        Assert.Equal(approved.AuthorizationId, purchase.Id);
        Assert.Equal(approved.MerchantAccountId, purchase.MerchantAccountId);
        Assert.Equal(42.50m, purchase.Amount.Amount);
        Assert.Equal("BRL", purchase.Amount.Currency);
        Assert.Equal(PurchaseStatus.Approved, purchase.Status);
        Assert.Equal(AuthorizedAt.AddTicks(1234560), purchase.AuthorizedAt);

        ProcessedMessage processed = Assert.Single(await worker.ProcessedMessagesAsync());
        Assert.Equal(envelope.EventId, processed.EventId);
        Assert.Equal(EventTypes.AuthorizationApproved, processed.Type);
    }

    [Fact]
    public async Task The_same_message_delivered_twice_is_processed_once()
    {
        string topic = await kafka.CreateTopicAsync();
        string groupId = NewGroupId();
        Guid cardId = Guid.NewGuid();
        EventEnvelope envelope = Envelope(Approved(cardId, 10m, AuthorizedAt));

        // At least once: e.g. the outbox publisher sent it again after a crash, with the same event id.
        await kafka.PublishAsync(topic, envelope);
        await kafka.PublishAsync(topic, envelope);

        await using RunningWorker worker = await RunningWorker.StartAsync(postgres, kafka, topic, groupId);
        await RunningWorker.WaitForAsync(() => Task.FromResult(kafka.CommittedCount(groupId, topic) == 2));

        Assert.Equal(2, worker.Logs.ConsumedOffsets().Length);
        Assert.Single((await worker.HistoryAsync(cardId)).Purchases);
        ProcessedMessage processed = Assert.Single(await worker.ProcessedMessagesAsync());
        Assert.Equal(envelope.EventId, processed.EventId);
    }

    [Fact]
    public async Task A_restarted_consumer_does_not_reprocess_the_messages_it_confirmed()
    {
        string topic = await kafka.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        string database = await postgres.CreateDatabaseAsync();
        Guid cardId = Guid.NewGuid();
        AuthorizationApproved first = Approved(cardId, 10m, AuthorizedAt);
        await kafka.PublishAsync(topic, Envelope(first));

        await using (RunningWorker worker = await RunningWorker.StartAsync(database, kafka, topic, groupId))
        {
            await worker.WaitForHistoryAsync(cardId, static h => h.Purchases.Count == 1);
            await RunningWorker.WaitForAsync(() => Task.FromResult(kafka.CommittedCount(groupId, topic) == 1));
        }

        AuthorizationApproved second = Approved(cardId, 20m, AuthorizedAt.AddMinutes(1));
        await kafka.PublishAsync(topic, Envelope(second));

        await using RunningWorker restarted = await RunningWorker.StartAsync(database, kafka, topic, groupId);
        CardHistory history = await restarted.WaitForHistoryAsync(cardId, static h => h.Purchases.Count == 2);
        await RunningWorker.WaitForAsync(() => Task.FromResult(kafka.CommittedCount(groupId, topic) == 2));

        // The first message (offset 0) was confirmed, so the new consumer started after it: only the new one arrived.
        Assert.Equal([1L], restarted.Logs.ConsumedOffsets());
        Assert.Equal([first.AuthorizationId, second.AuthorizationId], history.Purchases.Select(static p => p.Id));
        Assert.Equal(2, (await restarted.ProcessedMessagesAsync()).Count);
    }

    [Fact]
    public async Task An_event_of_an_unknown_type_is_skipped_and_the_next_message_is_processed()
    {
        string topic = await kafka.CreateTopicAsync(partitions: 1);
        string groupId = NewGroupId();
        Guid cardId = Guid.NewGuid();
        EventEnvelope approved = Envelope(Approved(cardId, 10m, AuthorizedAt));
        EventEnvelope unknown = approved with { EventId = Guid.CreateVersion7(), Type = "authorization.refunded" };
        await kafka.PublishAsync(topic, unknown);
        await kafka.PublishAsync(topic, approved);

        await using RunningWorker worker = await RunningWorker.StartAsync(postgres, kafka, topic, groupId);
        await worker.WaitForHistoryAsync(cardId, static h => h.Purchases.Count == 1);
        await RunningWorker.WaitForAsync(() => Task.FromResult(kafka.CommittedCount(groupId, topic) == 2));

        // Logged and ignored, not marked: a later version that knows the type can process it from a replay.
        ProcessedMessage processed = Assert.Single(await worker.ProcessedMessagesAsync());
        Assert.Equal(approved.EventId, processed.EventId);
    }

    [Fact]
    public async Task Every_decision_of_a_card_is_recorded_in_its_history()
    {
        string topic = await kafka.CreateTopicAsync();
        Guid cardId = Guid.NewGuid();
        Guid merchant = Guid.NewGuid();
        Guid capturedId = Guid.CreateVersion7();
        Guid voidedId = Guid.CreateVersion7();
        Guid declinedId = Guid.CreateVersion7();
        DateTimeOffset completedAt = AuthorizedAt.AddHours(1);
        IAuthorizationEvent[] events =
        [
            new AuthorizationApproved(capturedId, cardId, merchant, 30m, "BRL", AuthorizedAt),
            new AuthorizationApproved(voidedId, cardId, merchant, 20m, "BRL", AuthorizedAt.AddMinutes(1)),
            new AuthorizationDeclined(declinedId, cardId, merchant, 500m, "BRL", AuthorizedAt.AddMinutes(2), DeclineReasons.InsufficientFunds),
            new AuthorizationCaptured(capturedId, cardId, merchant, 30m, "BRL", AuthorizedAt, completedAt),
            new AuthorizationVoided(voidedId, cardId, merchant, 20m, "BRL", AuthorizedAt.AddMinutes(1), completedAt),
        ];
        foreach (IAuthorizationEvent integrationEvent in events)
        {
            await kafka.PublishAsync(topic, Envelope(integrationEvent));
        }

        await using RunningWorker worker = await RunningWorker.StartAsync(postgres, kafka, topic, NewGroupId());
        await RunningWorker.WaitForAsync(async () => (await worker.ProcessedMessagesAsync()).Count == events.Length);
        CardHistory history = await worker.HistoryAsync(cardId);

        Assert.Equal(
            [(capturedId, PurchaseStatus.Captured), (voidedId, PurchaseStatus.Voided), (declinedId, PurchaseStatus.Declined)],
            history.Purchases.Select(static p => (p.Id, p.Status)));
        Assert.Equal(DeclineReasons.InsufficientFunds, history.Declined.Single().DeclineReason);
        Assert.All(history.Approved, purchase => Assert.Equal(completedAt, purchase.CompletedAt));
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
}
