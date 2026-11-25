namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// Thrown by an <see cref="IIntegrationEventHandler{TEvent}"/> when the event can never be processed as it is (e.g. its
/// data breaks a rule of the consuming service), as opposed to a failure that may pass (e.g. the database is down).
/// </summary>
/// <remarks>
/// For now the consumer retries it like any other failure, so the event is never lost (ADR 0011); routing such events
/// to a dead-letter topic instead is a later change.
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
