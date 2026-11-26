using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using CardPlatform.Authorization.Api.Tests.Infrastructure;
using CardPlatform.Authorization.Infrastructure;
using CardPlatform.Authorization.Infrastructure.Persistence;
using CardPlatform.Contracts;
using CardPlatform.Contracts.Fraud;
using CardPlatform.Messaging.Consuming;

using Confluent.Kafka;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardPlatform.Authorization.Api.Tests.Messaging;

/// <summary>
/// The Authorization consuming <c>fraud-signals</c> (spec 006, ADR 0011): a <see cref="FraudSuspected"/> published as
/// the Fraud Worker's outbox publisher publishes it (key = card id, value = the envelope) blocks the card once per event
/// id. <c>fraud-signals</c> is shared by the tests of the collection, so each test looks only at its own messages.
/// </summary>
[Collection(KafkaAuthorizationApiCollectionFixture.Name)]
public sealed class FraudSignalsConsumerTests(KafkaAuthorizationApiFactory factory)
{
    private const string HandlerCategory = "CardPlatform.Authorization.Infrastructure.Consuming.FraudSuspectedHandler";
    private const string ProcessorCategory = "CardPlatform.Messaging.Consuming.IntegrationEventProcessor";
    private const int CardBlockedLogId = 1;
    private const int UnknownCardLogId = 2;
    private const int DuplicateEventLogId = 103;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public void Consumer_of_fraud_signals_runs()
    {
        Assert.Contains(
            factory.Services.GetServices<IHostedService>(),
            service => service is IntegrationEventConsumer<AuthorizationDbContext>);
    }

    [Fact]
    public async Task A_published_fraud_suspected_blocks_the_card()
    {
        Guid cardId = await CreateCardAsync();
        EventEnvelope envelope = Envelope(Suspected(cardId));

        await PublishAsync(envelope);

        await WaitUntilAsync(async () => await CardStatusAsync(cardId) == "blocked", $"Card {cardId} was not blocked.");

        // GET /cards/{id} keeps its contract: the suspicion is only logged.
        JsonElement card = await _client.GetFromJsonAsync<JsonElement>($"/cards/{cardId}");
        Assert.Equal(
            ["id", "ledgerAccountId", "currency", "status"],
            card.EnumerateObject().Select(static property => property.Name));

        ProcessedMessage processed = Assert.Single(await ProcessedMessagesAsync(envelope.EventId));
        Assert.Equal(EventTypes.FraudSuspected, processed.Type);

        CapturedLog blocked = Assert.Single(factory.Logs.OfEvent(HandlerCategory, CardBlockedLogId, envelope.EventId));
        Assert.Equal(LogLevel.Warning, blocked.Level);
        Assert.Equal(cardId, blocked.Values["CardId"]);
        Assert.Equal("velocity", blocked.Values["Rule"]);
        Assert.Equal("3 approved purchases of the card within 60 s (threshold: 3).", blocked.Values["Reason"]);
    }

    [Fact]
    public async Task The_same_message_delivered_twice_is_processed_once()
    {
        Guid cardId = await CreateCardAsync();
        EventEnvelope envelope = Envelope(Suspected(cardId));

        // At least once: e.g. the Fraud Worker's outbox publisher sent it again after a crash, with the same event id.
        await PublishAsync(envelope);
        TopicPartitionOffset second = await PublishAsync(envelope);

        await WaitUntilCommittedAsync(second);

        Assert.Equal("blocked", await CardStatusAsync(cardId));
        Assert.Single(await ProcessedMessagesAsync(envelope.EventId));
        Assert.Single(factory.Logs.OfEvent(HandlerCategory, CardBlockedLogId, envelope.EventId));
        Assert.Single(factory.Logs.OfEvent(ProcessorCategory, DuplicateEventLogId, envelope.EventId));
        Assert.DoesNotContain(
            factory.Logs.Entries,
            static entry => entry.Level >= LogLevel.Error
                && entry.Category.StartsWith("CardPlatform.Messaging.Consuming", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_fraud_suspected_of_an_unknown_card_is_logged_and_confirmed_and_the_next_message_is_processed()
    {
        Guid unknownCardId = Guid.NewGuid();
        Guid cardId = await CreateCardAsync();
        EventEnvelope unknown = Envelope(Suspected(unknownCardId));
        EventEnvelope known = Envelope(Suspected(cardId));

        // Both on the same partition, so the second is only consumed once the first was confirmed.
        TopicPartitionOffset first = await PublishAsync(unknown, partition: 0);
        TopicPartitionOffset second = await PublishAsync(known, partition: 0);
        Assert.True(second.Offset.Value > first.Offset.Value);

        await WaitUntilAsync(async () => await CardStatusAsync(cardId) == "blocked", $"Card {cardId} was not blocked.");
        await WaitUntilCommittedAsync(second);

        CapturedLog logged = Assert.Single(factory.Logs.OfEvent(HandlerCategory, UnknownCardLogId, unknown.EventId));
        Assert.Equal(LogLevel.Warning, logged.Level);
        Assert.Equal(unknownCardId, logged.Values["CardId"]);
        Assert.Single(await ProcessedMessagesAsync(unknown.EventId));
        Assert.Single(await ProcessedMessagesAsync(known.EventId));
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/cards/{unknownCardId}")).StatusCode);
    }

    private static FraudSuspected Suspected(Guid cardId)
    {
        return new FraudSuspected(
            cardId,
            Guid.CreateVersion7(),
            "velocity",
            "3 approved purchases of the card within 60 s (threshold: 3).",
            DateTimeOffset.UtcNow);
    }

    private static EventEnvelope Envelope(FraudSuspected suspected)
    {
        return EventEnvelope.Create(suspected, Guid.CreateVersion7(), suspected.DetectedAt);
    }

    /// <summary>Publishes <paramref name="envelope"/> on <c>fraud-signals</c> as the outbox publisher does (key = card id).</summary>
    private async Task<TopicPartitionOffset> PublishAsync(EventEnvelope envelope, int? partition = null)
    {
        _ = factory.Server; // The API creates fraud-signals on startup (Development).

        using IProducer<string, byte[]> producer =
            new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = factory.KafkaBootstrapServers }).Build();
        Message<string, byte[]> message = new()
        {
            Key = envelope.PartitionKey,
            Value = EventSerializer.SerializeToUtf8Bytes(envelope),
        };

        DeliveryResult<string, byte[]> delivered = partition is { } index
            ? await producer.ProduceAsync(new TopicPartition(Topics.FraudSignals, index), message)
            : await producer.ProduceAsync(Topics.FraudSignals, message);
        return delivered.TopicPartitionOffset;
    }

    /// <summary>Waits until the Authorization's consumer group committed the offset after <paramref name="message"/>.</summary>
    private Task WaitUntilCommittedAsync(TopicPartitionOffset message)
    {
        return WaitUntilAsync(
            () => Task.FromResult(CommittedOffset(message.TopicPartition) > message.Offset.Value),
            $"The consumer did not confirm {message}.");
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

    private async Task<List<ProcessedMessage>> ProcessedMessagesAsync(Guid eventId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>().ProcessedMessages
            .AsNoTracking()
            .Where(message => message.EventId == eventId)
            .ToListAsync();
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
