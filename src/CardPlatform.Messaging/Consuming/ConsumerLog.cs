using Microsoft.Extensions.Logging;

namespace CardPlatform.Messaging.Consuming;

internal static partial class ConsumerLog
{
    [LoggerMessage(
        EventId = 101,
        Level = LogLevel.Warning,
        Message = "Ignoring event {EventId} of unknown type {EventType} (version {Version}); it may come from a newer producer.")]
    public static partial void UnknownEventType(ILogger logger, Guid eventId, string eventType, int version);

    [LoggerMessage(
        EventId = 102,
        Level = LogLevel.Debug,
        Message = "Ignoring event {EventId} of type {EventType}: no handler is registered for it.")]
    public static partial void EventNotHandled(ILogger logger, Guid eventId, string eventType);

    [LoggerMessage(
        EventId = 103,
        Level = LogLevel.Information,
        Message = "Skipping event {EventId} of type {EventType}: it was already processed.")]
    public static partial void DuplicateEvent(ILogger logger, Guid eventId, string eventType);

    [LoggerMessage(
        EventId = 110,
        Level = LogLevel.Information,
        Message = "Consuming integration events from {Topics}.")]
    public static partial void Subscribed(ILogger logger, string topics);

    [LoggerMessage(
        EventId = 111,
        Level = LogLevel.Debug,
        Message = "Message {Topic} [{Partition}] @{Offset}: {Result}.")]
    public static partial void MessageConsumed(ILogger logger, string topic, int partition, long offset, EventProcessingResult result);

    [LoggerMessage(
        EventId = 112,
        Level = LogLevel.Error,
        Message = "Processing message {Topic} [{Partition}] @{Offset} failed (attempt {Attempt} of {MaxAttempts}); retrying it in {Delay}.")]
    public static partial void ProcessingFailed(
        ILogger logger,
        Exception exception,
        string topic,
        int partition,
        long offset,
        int attempt,
        int maxAttempts,
        TimeSpan delay);

    [LoggerMessage(
        EventId = 113,
        Level = LogLevel.Warning,
        Message = "Consuming from Kafka failed ({ConsecutiveFailures} in a row); trying again in {Delay}.")]
    public static partial void ConsumeFailed(ILogger logger, Exception exception, int consecutiveFailures, TimeSpan delay);

    [LoggerMessage(
        EventId = 114,
        Level = LogLevel.Warning,
        Message = "Committing offset {Offset} of {Topic} [{Partition}] failed; the message will be delivered again and skipped as a duplicate.")]
    public static partial void CommitFailed(ILogger logger, Exception exception, string topic, int partition, long offset);

    [LoggerMessage(
        EventId = 115,
        Level = LogLevel.Warning,
        Message = "Could not rewind {Topic} [{Partition}] to offset {Offset} after a failure (the partition may have been revoked); its new owner reads it from the last committed offset.")]
    public static partial void SeekFailed(ILogger logger, Exception exception, string topic, int partition, long offset);

    [LoggerMessage(
        EventId = 116,
        Level = LogLevel.Critical,
        Message = "The Kafka consumer hit a fatal error and cannot consume anymore; stopping the application so it is restarted.")]
    public static partial void FatalConsumerError(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 117,
        Level = LogLevel.Information,
        Message = "Consuming resumed after {ConsecutiveFailures} failed attempt(s).")]
    public static partial void Resumed(ILogger logger, int consecutiveFailures);

    [LoggerMessage(
        EventId = 118,
        Level = LogLevel.Information,
        Message = "Message {Topic} [{Partition}] @{Offset} was processed after {FailedAttempts} failed attempt(s).")]
    public static partial void Recovered(ILogger logger, string topic, int partition, long offset, int failedAttempts);

    [LoggerMessage(
        EventId = 120,
        Level = LogLevel.Error,
        Message = "Message {Topic} [{Partition}] @{Offset} (event {EventId}, type {EventType}) was sent to {DeadLetterTopic} after {Attempts} attempt(s) ({FailureKind}): {Reason}")]
    public static partial void DeadLettered(
        ILogger logger,
        Exception exception,
        string topic,
        int partition,
        long offset,
        Guid? eventId,
        string? eventType,
        string deadLetterTopic,
        int attempts,
        string failureKind,
        string reason);

    [LoggerMessage(
        EventId = 121,
        Level = LogLevel.Error,
        Message = "Sending message {Topic} [{Partition}] @{Offset} to {DeadLetterTopic} failed; it stays uncommitted and the send is retried in {Delay}.")]
    public static partial void DeadLetterFailed(
        ILogger logger,
        Exception exception,
        string topic,
        int partition,
        long offset,
        string deadLetterTopic,
        TimeSpan delay);
}
