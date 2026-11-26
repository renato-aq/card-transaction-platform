namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// Thrown by an <see cref="IIntegrationEventHandler{TEvent}"/> when the event can never be processed as it is (e.g. its
/// data breaks a rule of the consuming service), as opposed to a failure that may pass (e.g. the database is down).
/// </summary>
/// <remarks>
/// The consumer does not retry it: the message goes to its dead-letter topic (<c>&lt;topic&gt;.dlt</c>) after this first
/// attempt, with the exception's message as the reason, and the partition moves on (ADR 0011). Any other exception is
/// treated as temporary and retried up to <see cref="IntegrationEventConsumerOptions.MaxAttempts"/> times first.
/// </remarks>
public sealed class IntegrationEventRejectedException : Exception
{
    public IntegrationEventRejectedException()
    {
    }

    public IntegrationEventRejectedException(string message)
        : base(message)
    {
    }

    public IntegrationEventRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
