using System.Text.Json;

using CardPlatform.Messaging.Consuming;

using Confluent.Kafka;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Messaging.Tests.Consuming;

/// <summary>How the consumer counts the attempts of a failing message and classifies failures (ADR 0011).</summary>
public sealed class FailedMessageTrackerTests
{
    private static readonly TopicPartition Partition0 = new("topic", 0);
    private static readonly TopicPartition Partition1 = new("topic", 1);

    [Fact]
    public void Attempts_belong_to_the_message_and_are_not_reset_by_messages_of_other_partitions()
    {
        FailedMessageTracker tracker = new();
        TopicPartitionOffset failing = new(Partition0, 7);

        Assert.Equal(1, tracker.RecordFailure(failing));

        // Messages of another partition are processed (or fail) between the retries of the failing one.
        Assert.Equal(0, tracker.Clear(new TopicPartitionOffset(Partition1, 3)));
        Assert.Equal(2, tracker.RecordFailure(failing));
        Assert.Equal(1, tracker.RecordFailure(new TopicPartitionOffset(Partition1, 4)));
        Assert.Equal(1, tracker.Clear(new TopicPartitionOffset(Partition1, 4)));

        Assert.Equal(3, tracker.RecordFailure(failing));
    }

    [Fact]
    public void Another_offset_at_the_head_of_the_partition_starts_again_from_zero()
    {
        FailedMessageTracker tracker = new();
        tracker.RecordFailure(new TopicPartitionOffset(Partition0, 7));
        tracker.RecordFailure(new TopicPartitionOffset(Partition0, 7));

        Assert.Equal(1, tracker.RecordFailure(new TopicPartitionOffset(Partition0, 8)));
    }

    [Fact]
    public void Clearing_the_message_returns_its_failed_attempts_and_forgets_them()
    {
        FailedMessageTracker tracker = new();
        TopicPartitionOffset failing = new(Partition0, 7);
        tracker.RecordFailure(failing);
        tracker.RecordFailure(failing);

        Assert.Equal(2, tracker.Clear(failing));
        Assert.Equal(0, tracker.Clear(failing));
        Assert.Equal(1, tracker.RecordFailure(failing));
    }

    [Fact]
    public void A_failed_dead_letter_send_is_remembered_for_the_next_delivery_of_the_message_only()
    {
        FailedMessageTracker tracker = new();
        TopicPartitionOffset failing = new(Partition0, 7);
        InvalidOperationException exception = new("Database down.");
        tracker.RecordFailure(failing);

        Assert.Equal(1, tracker.RecordDeadLetterFailure(failing, exception, attempts: 5, permanent: false));
        Assert.Equal(2, tracker.RecordDeadLetterFailure(failing, exception, attempts: 5, permanent: false));

        FailedMessageTracker.PendingDeadLetter? pending = tracker.PendingDeadLetterOf(failing);
        Assert.NotNull(pending);
        Assert.Same(exception, pending.Exception);
        Assert.Equal(5, pending.Attempts);
        Assert.False(pending.Permanent);
        Assert.Null(tracker.PendingDeadLetterOf(new TopicPartitionOffset(Partition0, 8)));

        tracker.Clear(failing);
        Assert.Null(tracker.PendingDeadLetterOf(failing));
    }

    [Fact]
    public void Malformed_messages_and_rejected_events_are_permanent_failures_and_the_rest_temporary()
    {
        Assert.True(IntegrationEventConsumer<DbContext>.IsPermanent(new JsonException("Not an envelope.")));
        Assert.True(IntegrationEventConsumer<DbContext>.IsPermanent(new IntegrationEventRejectedException("Invalid currency.")));
        Assert.False(IntegrationEventConsumer<DbContext>.IsPermanent(new InvalidOperationException("Database down.")));
        Assert.False(IntegrationEventConsumer<DbContext>.IsPermanent(new TimeoutException()));
    }
}
