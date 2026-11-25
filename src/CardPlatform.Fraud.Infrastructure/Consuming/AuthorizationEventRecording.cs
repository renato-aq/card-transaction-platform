using CardPlatform.Contracts;
using CardPlatform.Fraud.Application.Purchases;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Messaging.Consuming;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Fraud.Infrastructure.Consuming;

/// <summary>What the handlers of the authorization events do with the outcome of their use case.</summary>
internal static partial class AuthorizationEventRecording
{
    /// <summary>
    /// Logs the outcome, or throws <see cref="IntegrationEventRejectedException"/> when the use case rejected the event:
    /// its data can never be recorded as it is, so the event must not be marked as processed (ADR 0011).
    /// </summary>
    public static void Complete(Result<RecordingOutcome> result, EventEnvelope envelope, ILogger logger)
    {
        if (result.IsFailure)
        {
            throw new IntegrationEventRejectedException(
                $"Event {envelope.EventId} ({envelope.Type}) was rejected: {result.Error.Code}: {result.Error.Message}");
        }

        Recorded(logger, envelope.EventId, envelope.Type, result.Value);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Event {EventId} ({EventType}) applied to the card's purchase history: {Outcome}.")]
    private static partial void Recorded(ILogger logger, Guid eventId, string eventType, RecordingOutcome outcome);
}
