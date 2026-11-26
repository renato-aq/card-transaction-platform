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
        ThrowIfRejected(result, envelope);

        Recorded(logger, envelope.EventId, envelope.Type, result.Value);
    }

    /// <summary>
    /// As <see cref="Complete(Result{RecordingOutcome}, EventEnvelope, ILogger)"/> for an approved purchase, and logs the
    /// fraud suspicion it raised, if any.
    /// </summary>
    public static void Complete(Result<ApprovedPurchaseRecording> result, EventEnvelope envelope, ILogger logger)
    {
        ThrowIfRejected(result, envelope);

        Recorded(logger, envelope.EventId, envelope.Type, result.Value.Outcome);
        if (result.Value.Suspicion is { } suspicion)
        {
            FraudSuspected(logger, suspicion.CardId, suspicion.AuthorizationId, suspicion.Rule, suspicion.Reason);
        }
    }

    private static void ThrowIfRejected(Result result, EventEnvelope envelope)
    {
        if (result.IsFailure)
        {
            throw new IntegrationEventRejectedException(
                $"Event {envelope.EventId} ({envelope.Type}) was rejected: {result.Error.Code}: {result.Error.Message}");
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Event {EventId} ({EventType}) applied to the card's purchase history: {Outcome}.")]
    private static partial void Recorded(ILogger logger, Guid eventId, string eventType, RecordingOutcome outcome);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Fraud suspected on card {CardId} for authorization {AuthorizationId} ({Rule}): {Reason}")]
    private static partial void FraudSuspected(ILogger logger, Guid cardId, Guid authorizationId, string rule, string reason);
}
