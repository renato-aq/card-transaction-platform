namespace CardPlatform.Messaging.Consuming;

/// <summary>What <see cref="IntegrationEventProcessor{TDbContext}.ProcessAsync"/> did with a message.</summary>
public enum EventProcessingResult
{
    /// <summary>The handler ran and its changes were committed together with the processed mark.</summary>
    Processed,

    /// <summary>The event id was already processed (a redelivery or a duplicate); nothing was done.</summary>
    Duplicate,

    /// <summary>
    /// No handler is registered for the event type (unknown to this version, or not of interest); the message was logged
    /// and ignored without being marked, so a later version that handles the type can process it from a replay.
    /// </summary>
    Skipped,
}
