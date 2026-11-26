namespace CardPlatform.Messaging.Kafka;

/// <summary>
/// Headers the integration event consumer adds to a message it sends to a dead-letter topic (<c>&lt;topic&gt;.dlt</c>,
/// ADR 0011), next to the original message's own headers. Every value is UTF-8 text.
/// </summary>
public static class DeadLetterHeaders
{
    /// <summary>Common prefix of the headers below; headers with it are replaced, not repeated, when a replayed message fails again.</summary>
    public const string Prefix = "dlt-";

    /// <summary>Why the message failed: the message of the last exception (at most <see cref="MaxReasonLength"/> characters).</summary>
    public const string Reason = "dlt-reason";

    /// <summary>The full name of the type of the last exception.</summary>
    public const string ExceptionType = "dlt-exception-type";

    /// <summary><see cref="PermanentFailure"/> or <see cref="RetriesExhausted"/>.</summary>
    public const string FailureKind = "dlt-failure-kind";

    /// <summary>How many times the message was processed before it was dead-lettered (decimal).</summary>
    public const string Attempts = "dlt-attempts";

    /// <summary>The topic the message was consumed from.</summary>
    public const string OriginalTopic = "dlt-original-topic";

    /// <summary>The partition the message was consumed from (decimal).</summary>
    public const string OriginalPartition = "dlt-original-partition";

    /// <summary>The offset of the message in its original partition (decimal).</summary>
    public const string OriginalOffset = "dlt-original-offset";

    /// <summary>The consumer group that gave up on the message.</summary>
    public const string ConsumerGroup = "dlt-consumer-group";

    /// <summary>When the message was dead-lettered (UTC, ISO 8601 round-trip format).</summary>
    public const string FailedAt = "dlt-failed-at";

    /// <summary><see cref="FailureKind"/> of a message that can never be processed (malformed, or rejected by its handler).</summary>
    public const string PermanentFailure = "permanent";

    /// <summary><see cref="FailureKind"/> of a message that kept failing with a temporary error until no attempt was left.</summary>
    public const string RetriesExhausted = "retries-exhausted";

    /// <summary>Longest <see cref="Reason"/> written; longer messages are cut.</summary>
    public const int MaxReasonLength = 1000;
}
