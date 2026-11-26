using System.Diagnostics.CodeAnalysis;

using CardPlatform.Contracts;

namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// Reacts to one type of integration event consumed from Kafka. Registered with
/// <see cref="MessagingHostingExtensions.AddIntegrationEventHandler{TEvent, THandler}"/> and called by the consumer of
/// <see cref="MessagingHostingExtensions.AddIntegrationEventConsumer{TDbContext}"/> once per event id (ADR 0011).
/// </summary>
/// <remarks>
/// The handler is resolved from a new scope for each message, and runs inside a transaction of that scope's
/// <c>DbContext</c>, the one that also records the event as processed: whatever it saves with that context is committed
/// together with the mark, or not at all. It must not commit or open transactions of its own. Throwing leaves the event
/// unprocessed: nothing is committed and the offset is not committed. The consumer retries the event, up to
/// <see cref="IntegrationEventConsumerOptions.MaxAttempts"/> times, and then sends it to the dead-letter topic; throwing
/// <see cref="IntegrationEventRejectedException"/> sends it there at once.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "It handles integration events, not .NET events; 'integration event handler' is the usual name.")]
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    /// <param name="integrationEvent">The payload of the event.</param>
    /// <param name="envelope">The envelope it came in (event id, type, version, when it occurred).</param>
    /// <param name="cancellationToken">Cancelled when the host is stopping.</param>
    Task HandleAsync(TEvent integrationEvent, EventEnvelope envelope, CancellationToken cancellationToken);
}
