using System.Text.Json;

using CardPlatform.Contracts;
using CardPlatform.Contracts.Fraud;
using CardPlatform.Messaging.Consuming;
using CardPlatform.Messaging.Outbox;
using CardPlatform.Messaging.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Messaging.Tests.Consuming;

/// <summary>
/// The idempotent processing of a consumed message against a real PostgreSQL, without Kafka (ADR 0011): the processed
/// mark and the handler's changes are committed together, once per event id.
/// </summary>
[Collection(MessagingCollectionFixture.Name)]
public sealed class IntegrationEventProcessorTests(MessagingFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public Task InitializeAsync()
    {
        return fixture.ResetOutboxAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task An_event_is_handled_and_marked_as_processed_in_the_same_transaction()
    {
        await using ServiceProvider services = fixture.CreateProcessorServices();
        EventEnvelope envelope = MessagingFixture.NewEnvelope(Guid.NewGuid());

        EventProcessingResult result = await Processor(services).ProcessAsync(EventSerializer.SerializeToUtf8Bytes(envelope), CancellationToken.None);

        Assert.Equal(EventProcessingResult.Processed, result);
        ProcessedMessage processed = Assert.Single(await fixture.ReadProcessedMessagesAsync());
        Assert.Equal(envelope.EventId, processed.EventId);
        Assert.Equal(EventTypes.AuthorizationApproved, processed.Type);
        Assert.Equal(0, processed.ProcessedAt.Ticks % TimeSpan.TicksPerMicrosecond);
        OutboxMessage effect = Assert.Single(await fixture.ReadOutboxAsync());
        Assert.Equal(envelope.EventId, effect.EventId);
    }

    [Fact]
    public async Task A_second_copy_of_an_event_is_skipped_without_calling_the_handler()
    {
        await using ServiceProvider services = fixture.CreateProcessorServices();
        byte[] message = EventSerializer.SerializeToUtf8Bytes(MessagingFixture.NewEnvelope(Guid.NewGuid()));

        EventProcessingResult first = await Processor(services).ProcessAsync(message, CancellationToken.None);
        EventProcessingResult second = await Processor(services).ProcessAsync(message, CancellationToken.None);

        Assert.Equal(EventProcessingResult.Processed, first);
        Assert.Equal(EventProcessingResult.Duplicate, second);
        Assert.Single(services.GetRequiredService<HandlerProbe>().Calls);
        Assert.Single(await fixture.ReadProcessedMessagesAsync());
        Assert.Single(await fixture.ReadOutboxAsync());
    }

    [Fact]
    public async Task When_the_handler_fails_nothing_is_committed_and_the_event_can_be_processed_again()
    {
        await using ServiceProvider services = fixture.CreateProcessorServices();
        HandlerProbe probe = services.GetRequiredService<HandlerProbe>();
        probe.OnHandle = static _ => throw new InvalidOperationException("Handler failed.");
        byte[] message = EventSerializer.SerializeToUtf8Bytes(MessagingFixture.NewEnvelope(Guid.NewGuid()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Processor(services).ProcessAsync(message, CancellationToken.None));

        Assert.Empty(await fixture.ReadProcessedMessagesAsync());
        Assert.Empty(await fixture.ReadOutboxAsync());

        probe.OnHandle = static _ => Task.CompletedTask;
        Assert.Equal(EventProcessingResult.Processed, await Processor(services).ProcessAsync(message, CancellationToken.None));
        Assert.Single(await fixture.ReadProcessedMessagesAsync());
        Assert.Single(await fixture.ReadOutboxAsync());
    }

    [Fact]
    public async Task Concurrent_copies_of_an_event_are_processed_once()
    {
        // E.g. after a rebalance, the old and the new owner of a partition both process the same message.
        await using ServiceProvider services = fixture.CreateProcessorServices();
        HandlerProbe probe = services.GetRequiredService<HandlerProbe>();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        probe.OnHandle = async _ =>
        {
            entered.TrySetResult();
            await release.Task;
        };
        byte[] message = EventSerializer.SerializeToUtf8Bytes(MessagingFixture.NewEnvelope(Guid.NewGuid()));

        Task<EventProcessingResult> first = Processor(services).ProcessAsync(message, CancellationToken.None);
        await entered.Task.WaitAsync(Timeout);

        // The first copy holds the processed row inside its open transaction: the second one waits on it.
        Task<EventProcessingResult> second = Processor(services).ProcessAsync(message, CancellationToken.None);
        await Task.Delay(500);
        Assert.False(second.IsCompleted);

        release.SetResult();
        EventProcessingResult[] results = await Task.WhenAll(first, second).WaitAsync(Timeout);

        Assert.Equal([EventProcessingResult.Processed, EventProcessingResult.Duplicate], results);
        Assert.Single(probe.Calls);
        Assert.Single(await fixture.ReadProcessedMessagesAsync());
        Assert.Single(await fixture.ReadOutboxAsync());
    }

    [Fact]
    public async Task An_event_of_an_unknown_type_is_skipped_without_being_marked()
    {
        await using ServiceProvider services = fixture.CreateProcessorServices();
        EventEnvelope envelope = MessagingFixture.NewEnvelope(Guid.NewGuid()) with { Type = "authorization.refunded", Version = 1 };

        EventProcessingResult result = await Processor(services).ProcessAsync(EventSerializer.SerializeToUtf8Bytes(envelope), CancellationToken.None);

        Assert.Equal(EventProcessingResult.Skipped, result);
        Assert.Empty(services.GetRequiredService<HandlerProbe>().Calls);
        Assert.Empty(await fixture.ReadProcessedMessagesAsync());
    }

    [Fact]
    public async Task An_event_type_without_a_handler_is_skipped_without_being_marked()
    {
        await using ServiceProvider services = fixture.CreateProcessorServices();
        FraudSuspected suspected = new(Guid.NewGuid(), Guid.CreateVersion7(), "velocity", "5 purchases in 60 s", DateTimeOffset.UtcNow);
        EventEnvelope envelope = EventEnvelope.Create(suspected, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        EventProcessingResult result = await Processor(services).ProcessAsync(EventSerializer.SerializeToUtf8Bytes(envelope), CancellationToken.None);

        Assert.Equal(EventProcessingResult.Skipped, result);
        Assert.Empty(await fixture.ReadProcessedMessagesAsync());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"eventId\":\"6f1c3c52-35a8-4f0c-9f0e-4e0f1b1f3a11\",\"type\":\"authorization.approved\"}")]
    public async Task A_message_that_is_not_an_envelope_fails_without_marking_anything(string value)
    {
        await using ServiceProvider services = fixture.CreateProcessorServices();

        await Assert.ThrowsAnyAsync<JsonException>(
            () => Processor(services).ProcessAsync(System.Text.Encoding.UTF8.GetBytes(value), CancellationToken.None));

        Assert.Empty(await fixture.ReadProcessedMessagesAsync());
    }

    [Fact]
    public void Two_handlers_for_the_same_event_type_are_refused()
    {
        using ServiceProvider services = fixture.CreateProcessorServices(services =>
            services.AddIntegrationEventHandler<Contracts.Authorizations.AuthorizationApproved, RecordingApprovedHandler>());

        Assert.Throws<InvalidOperationException>(() => Processor(services));
    }

    private static IntegrationEventProcessor<TestOutboxDbContext> Processor(IServiceProvider services)
    {
        return services.GetRequiredService<IntegrationEventProcessor<TestOutboxDbContext>>();
    }
}
