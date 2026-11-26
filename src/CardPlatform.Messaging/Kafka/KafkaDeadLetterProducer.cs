using System.Globalization;
using System.Text;

using CardPlatform.Contracts;

using Confluent.Kafka;

namespace CardPlatform.Messaging.Kafka;

/// <summary>
/// Sends a message the integration event consumer gave up on to the dead-letter topic of its topic
/// (<see cref="Topics.DeadLetterOf"/>, ADR 0011), with the producer registered by the Aspire integration (idempotent,
/// <c>acks=all</c>, see <see cref="KafkaProducerDefaults"/>): the original key and value, byte for byte (so a malformed
/// value is kept as it arrived), the original headers, and the error headers of <see cref="DeadLetterHeaders"/>, whose
/// reason and exception type come from <see cref="CauseOf"/>.
/// </summary>
public sealed class KafkaDeadLetterProducer(IProducer<string, byte[]> producer, string consumerGroup, TimeProvider timeProvider)
{
    /// <summary>The consumer group written to <see cref="DeadLetterHeaders.ConsumerGroup"/>.</summary>
    public string ConsumerGroup { get; } = consumerGroup;

    /// <summary>
    /// Sends <paramref name="failed"/> to its dead-letter topic and completes when the broker acknowledged it (all
    /// in-sync replicas), or throws <see cref="ProduceException{TKey, TValue}"/>; returns the dead-letter topic.
    /// </summary>
    /// <param name="failed">The message as it was consumed.</param>
    /// <param name="exception">The last failure.</param>
    /// <param name="attempts">How many times the message was processed.</param>
    /// <param name="permanent">Whether the failure is permanent (<see langword="false"/>: the attempts ran out).</param>
    /// <param name="cancellationToken">Cancelled when the host is stopping.</param>
    public async Task<string> ProduceAsync(
        ConsumeResult<string, byte[]> failed,
        Exception exception,
        int attempts,
        bool permanent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failed);
        ArgumentNullException.ThrowIfNull(exception);

        string deadLetterTopic = Topics.DeadLetterOf(failed.Topic);

        Headers headers = [];
        if (failed.Message.Headers is { } original)
        {
            // A message replayed from the dead-letter topic that fails again carries the previous error headers; they
            // are replaced by the new ones instead of repeated.
            foreach (IHeader header in original.Where(static header =>
                !header.Key.StartsWith(DeadLetterHeaders.Prefix, StringComparison.Ordinal)))
            {
                headers.Add(header.Key, header.GetValueBytes());
            }
        }

        Exception cause = CauseOf(exception, permanent);
        Add(headers, DeadLetterHeaders.Reason, Truncate(cause.Message, DeadLetterHeaders.MaxReasonLength));
        Add(headers, DeadLetterHeaders.ExceptionType, cause.GetType().FullName ?? cause.GetType().Name);
        Add(headers, DeadLetterHeaders.FailureKind, permanent ? DeadLetterHeaders.PermanentFailure : DeadLetterHeaders.RetriesExhausted);
        Add(headers, DeadLetterHeaders.Attempts, attempts.ToString(CultureInfo.InvariantCulture));
        Add(headers, DeadLetterHeaders.OriginalTopic, failed.Topic);
        Add(headers, DeadLetterHeaders.OriginalPartition, failed.Partition.Value.ToString(CultureInfo.InvariantCulture));
        Add(headers, DeadLetterHeaders.OriginalOffset, failed.Offset.Value.ToString(CultureInfo.InvariantCulture));
        Add(headers, DeadLetterHeaders.ConsumerGroup, ConsumerGroup);
        Add(headers, DeadLetterHeaders.FailedAt, timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));

        Message<string, byte[]> message = new()
        {
            Key = failed.Message.Key,
            Value = failed.Message.Value,
            Headers = headers,
        };

        await producer.ProduceAsync(deadLetterTopic, message, cancellationToken).ConfigureAwait(false);
        return deadLetterTopic;
    }

    /// <summary>
    /// The exception whose message and type describe a failure: for a temporary failure its root cause
    /// (<see cref="Exception.GetBaseException"/>), so a wrapper such as EF's <c>RetryLimitExceededException</c> ("See the
    /// inner exception") does not hide the Npgsql error; a permanent failure is recognized by its own type
    /// (<c>JsonException</c>, <see cref="Consuming.IntegrationEventRejectedException"/>), so it is kept as it is (a
    /// <c>JsonException</c> wraps an internal reader exception).
    /// </summary>
    internal static Exception CauseOf(Exception exception, bool permanent)
    {
        return permanent ? exception : exception.GetBaseException();
    }

    private static void Add(Headers headers, string key, string value)
    {
        headers.Add(key, Encoding.UTF8.GetBytes(value));
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
