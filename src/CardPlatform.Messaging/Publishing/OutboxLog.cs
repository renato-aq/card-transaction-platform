using Microsoft.Extensions.Logging;

namespace CardPlatform.Messaging.Publishing;

internal static partial class OutboxLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Another instance is publishing the outbox of {DbContext}; skipping this cycle.")]
    public static partial void LockHeldElsewhere(ILogger logger, string dbContext);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Publishing outbox message {MessageId} (event {EventId}) to {Topic} failed on attempt {Attempts}.")]
    public static partial void PublishFailed(
        ILogger logger,
        Exception exception,
        long messageId,
        Guid eventId,
        string topic,
        int attempts);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Published {Count} outbox message(s) of {DbContext}.")]
    public static partial void Published(ILogger logger, int count, string dbContext);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Outbox publishing cycle of {DbContext} failed ({ConsecutiveFailures} in a row); retrying in {Delay}.")]
    public static partial void CycleFailed(
        ILogger logger,
        Exception exception,
        string dbContext,
        int consecutiveFailures,
        TimeSpan delay);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "The Kafka producer {Producer} hit a fatal error and cannot send anymore; it is disposed once no send uses it and the next send uses a new producer.")]
    public static partial void ProducerReplaced(ILogger logger, Exception exception, string producer);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message = "Outbox publishing of {DbContext} resumed after {ConsecutiveFailures} failed cycle(s).")]
    public static partial void Resumed(ILogger logger, string dbContext, int consecutiveFailures);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Information,
        Message = "Kafka topic {Topic} created with {Partitions} partition(s).")]
    public static partial void TopicCreated(ILogger logger, string topic, int partitions);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Debug,
        Message = "Kafka topic {Topic} already exists.")]
    public static partial void TopicExists(ILogger logger, string topic);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Warning,
        Message = "Could not create the Kafka topics {Topics}; publishing will fail until they exist.")]
    public static partial void TopicsNotCreated(ILogger logger, Exception exception, string topics);
}
