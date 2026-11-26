using System.Collections.Concurrent;

using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Messaging.Consuming;
using CardPlatform.Messaging.Outbox;

namespace CardPlatform.Messaging.Tests.Infrastructure;

/// <summary>What the test handlers did, and a hook to make them fail or wait (singleton, shared by their scopes).</summary>
public sealed class HandlerProbe
{
    /// <summary>The event ids handled, in the order the handler was called (including calls that failed).</summary>
    public ConcurrentQueue<Guid> Calls { get; } = new();

    /// <summary>Runs at the start of every call; throwing from it makes the handler fail.</summary>
    public Func<EventEnvelope, Task> OnHandle { get; set; } = static _ => Task.CompletedTask;
}

/// <summary>
/// Handles <see cref="AuthorizationApproved"/> by writing an outbox row for it with the scope's context: an effect that
/// must be committed together with the processed mark, or not at all.
/// </summary>
public sealed class RecordingApprovedHandler(TestOutboxDbContext dbContext, HandlerProbe probe)
    : IIntegrationEventHandler<AuthorizationApproved>
{
    public const string EffectTopic = "effects";

    public async Task HandleAsync(AuthorizationApproved integrationEvent, EventEnvelope envelope, CancellationToken cancellationToken)
    {
        probe.Calls.Enqueue(envelope.EventId);
        await probe.OnHandle(envelope);
        dbContext.OutboxMessages.Add(OutboxMessage.Create(envelope, EffectTopic, DateTimeOffset.UtcNow));
    }
}
