using Confluent.Kafka;

namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// What the consumer knows about the message that is failing at the head of each partition (ADR 0011): how many times it
/// was processed, and whether it is waiting to be sent to the dead-letter topic. Attempts belong to the message (topic,
/// partition and offset): messages of other partitions processed in between do not reset them, and a different offset at
/// the head of the partition starts again from zero. Used only by the consumer's loop, from one thread.
/// </summary>
internal sealed class FailedMessageTracker
{
    private readonly Dictionary<TopicPartition, Entry> _entries = [];

    /// <summary>Records one more failed attempt of the message at <paramref name="message"/>; returns its attempts so far.</summary>
    public int RecordFailure(TopicPartitionOffset message)
    {
        Entry entry = Current(message);
        entry.Attempts++;
        return entry.Attempts;
    }

    /// <summary>
    /// Records that sending the message at <paramref name="message"/> to the dead-letter topic failed, after
    /// <paramref name="attempts"/> attempts ended with <paramref name="exception"/>, so its next delivery is sent there
    /// again without being processed; returns how many sends failed so far.
    /// </summary>
    public int RecordDeadLetterFailure(TopicPartitionOffset message, Exception exception, int attempts, bool permanent)
    {
        Entry entry = Current(message);
        entry.Attempts = attempts;
        entry.PendingDeadLetter = new PendingDeadLetter(exception, attempts, permanent);
        entry.DeadLetterFailures++;
        return entry.DeadLetterFailures;
    }

    /// <summary>The dead-lettering still owed for the message at <paramref name="message"/>, if a send failed before.</summary>
    public PendingDeadLetter? PendingDeadLetterOf(TopicPartitionOffset message)
    {
        return _entries.TryGetValue(message.TopicPartition, out Entry? entry) && entry.Offset == message.Offset
            ? entry.PendingDeadLetter
            : null;
    }

    /// <summary>
    /// Forgets the failures of the message at <paramref name="message"/> once it was processed or dead-lettered;
    /// returns how many attempts had failed (0 when none).
    /// </summary>
    public int Clear(TopicPartitionOffset message)
    {
        if (!_entries.TryGetValue(message.TopicPartition, out Entry? entry))
        {
            return 0;
        }

        _entries.Remove(message.TopicPartition);
        return entry.Offset == message.Offset ? entry.Attempts : 0;
    }

    private Entry Current(TopicPartitionOffset message)
    {
        if (!_entries.TryGetValue(message.TopicPartition, out Entry? entry) || entry.Offset != message.Offset)
        {
            entry = new Entry(message.Offset);
            _entries[message.TopicPartition] = entry;
        }

        return entry;
    }

    /// <summary>A dead-lettering whose send failed: the failure to report when it is sent again.</summary>
    public sealed record PendingDeadLetter(Exception Exception, int Attempts, bool Permanent);

    private sealed class Entry(Offset offset)
    {
        public Offset Offset { get; } = offset;

        public int Attempts { get; set; }

        public int DeadLetterFailures { get; set; }

        public PendingDeadLetter? PendingDeadLetter { get; set; }
    }
}
