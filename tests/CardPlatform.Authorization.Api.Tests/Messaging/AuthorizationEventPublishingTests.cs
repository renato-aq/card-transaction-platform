using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using CardPlatform.Authorization.Api.Tests.Infrastructure;
using CardPlatform.Authorization.Infrastructure.Persistence;
using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Messaging.Outbox;

using Confluent.Kafka;

using CardPlatform.Messaging.Publishing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CardPlatform.Authorization.Api.Tests.Messaging;

/// <summary>
/// End to end: decisions made through the API reach the <c>authorizations</c> topic through the outbox publisher (spec
/// 002 of the outbox task, ADR 0010).
/// </summary>
[Collection(KafkaAuthorizationApiCollectionFixture.Name)]
public sealed class AuthorizationEventPublishingTests(KafkaAuthorizationApiFactory factory)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _client = factory.CreateClient();
    private readonly HttpClient _ledger = factory.Ledger.CreateClient();

    [Fact]
    public async Task Approved_and_captured_purchase_is_published_on_the_authorizations_topic_keyed_by_card_id_in_order()
    {
        Guid cardId = await CreateFundedCardAsync(100m);
        Guid merchantAccountId = await OpenLedgerAccountAsync("merchant");

        HttpResponseMessage authorized = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/authorizations")
        {
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } },
            Content = JsonContent.Create(new { cardId, merchantAccountId, amount = 30m, currency = "BRL" }),
        });
        Assert.Equal(HttpStatusCode.Created, authorized.StatusCode);
        Guid authorizationId = (await authorized.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(
            HttpStatusCode.OK,
            (await _client.PostAsync($"/authorizations/{authorizationId}/capture", content: null)).StatusCode);

        string key = cardId.ToString("D", CultureInfo.InvariantCulture);
        List<ConsumeResult<string, byte[]>> ofCard = await ConsumeAsync(Topics.Authorizations, key, count: 2);

        Assert.All(ofCard, result => Assert.Equal(key, result.Message.Key));
        Assert.Single(ofCard.Select(result => result.Partition.Value).Distinct());
        EventEnvelope[] envelopes = [.. ofCard.Select(result => EventSerializer.Deserialize(result.Message.Value))];
        Assert.Equal([EventTypes.AuthorizationApproved, EventTypes.AuthorizationCaptured], envelopes.Select(e => e.Type));
        Assert.Equal(authorizationId, EventSerializer.DeserializePayload<AuthorizationApproved>(envelopes[0]).AuthorizationId);
        Assert.Equal(authorizationId, EventSerializer.DeserializePayload<AuthorizationCaptured>(envelopes[1]).AuthorizationId);

        List<OutboxMessage> rows = await WaitUntilPublishedAsync(key);
        Assert.Equal(envelopes.Select(e => e.EventId), rows.Select(row => row.EventId));
    }

    [Fact]
    public void Topics_are_created_on_startup_in_development_with_three_partitions()
    {
        _ = factory.Server;
        using IAdminClient admin =
            new AdminClientBuilder(new AdminClientConfig { BootstrapServers = factory.KafkaBootstrapServers }).Build();

        foreach (string topic in new[] { Topics.Authorizations, Topics.FraudSignals })
        {
            TopicMetadata metadata = Assert.Single(admin.GetMetadata(topic, TimeSpan.FromSeconds(10)).Topics);
            Assert.Equal(ErrorCode.NoError, metadata.Error.Code);
            Assert.Equal(3, metadata.Partitions.Count);
        }
    }

    [Fact]
    public void Publisher_runs_and_readiness_does_not_depend_on_kafka()
    {
        Assert.Contains(
            factory.Services.GetServices<IHostedService>(),
            service => service is OutboxPublisher<AuthorizationDbContext>);

        // A Kafka outage only delays events (ADR 0010), so the Aspire Kafka health check is off by default.
        HealthCheckServiceOptions healthChecks = factory.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        Assert.DoesNotContain(
            healthChecks.Registrations,
            registration => registration.Name.Contains("kafka", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<OutboxMessage>> WaitUntilPublishedAsync(string partitionKey)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
            List<OutboxMessage> rows = await dbContext.OutboxMessages.AsNoTracking()
                .Where(message => message.PartitionKey == partitionKey)
                .OrderBy(message => message.Id)
                .ToListAsync();
            if (rows.Count > 0 && rows.All(row => row.PublishedAt is not null))
            {
                return rows;
            }

            Assert.True(DateTime.UtcNow < deadline, "The outbox rows were not marked as published.");
            await Task.Delay(100);
        }
    }

    private Task<List<ConsumeResult<string, byte[]>>> ConsumeAsync(string topic, string key, int count)
    {
        return Task.Run(() =>
        {
            using IConsumer<string, byte[]> consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
            {
                BootstrapServers = factory.KafkaBootstrapServers,
                GroupId = "test-" + Guid.NewGuid().ToString("N"),
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false,
            }).Build();
            consumer.Subscribe(topic);

            List<ConsumeResult<string, byte[]>> results = [];
            DateTime deadline = DateTime.UtcNow + Timeout;
            while (results.Count < count && DateTime.UtcNow < deadline)
            {
                ConsumeResult<string, byte[]>? result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                if (result is { IsPartitionEOF: false, Message: not null } && result.Message.Key == key)
                {
                    results.Add(result);
                }
            }

            consumer.Close();
            Assert.True(results.Count >= count, $"Only {results.Count} of {count} events of card {key} arrived on {topic}.");
            return results;
        });
    }

    private async Task<Guid> CreateFundedCardAsync(decimal balance)
    {
        HttpResponseMessage cardResponse = await _client.PostAsJsonAsync("/cards", new { currency = "BRL" });
        Assert.Equal(HttpStatusCode.Created, cardResponse.StatusCode);
        JsonElement card = await cardResponse.Content.ReadFromJsonAsync<JsonElement>();
        Guid cardAccountId = card.GetProperty("ledgerAccountId").GetGuid();

        Guid settlement = await OpenLedgerAccountAsync("settlement");
        HttpResponseMessage topUp = await _ledger.PostAsJsonAsync("/transactions", new
        {
            currency = "BRL",
            entries = new[]
            {
                new { accountId = settlement, direction = "debit", amount = balance },
                new { accountId = cardAccountId, direction = "credit", amount = balance },
            },
        });
        Assert.Equal(HttpStatusCode.Created, topUp.StatusCode);

        return card.GetProperty("id").GetGuid();
    }

    private async Task<Guid> OpenLedgerAccountAsync(string type)
    {
        HttpResponseMessage response = await _ledger.PostAsJsonAsync("/accounts", new { type, currency = "BRL" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
