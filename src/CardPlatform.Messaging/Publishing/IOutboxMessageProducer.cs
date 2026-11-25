using CardPlatform.Messaging.Outbox;

namespace CardPlatform.Messaging.Publishing;

/// <summary>Sends one outbox row to the message broker.</summary>
public interface IOutboxMessageProducer
{
    /// <summary>
    /// Publishes <paramref name="message"/> on its topic, keyed by its partition key, and completes only when the broker
    /// acknowledged it.
    /// </summary>
    /// <exception cref="Exception">The event could not be published; it may still have reached the broker.</exception>
    Task ProduceAsync(OutboxMessage message, CancellationToken cancellationToken);
}
