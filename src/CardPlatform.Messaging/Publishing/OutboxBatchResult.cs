namespace CardPlatform.Messaging.Publishing;

/// <summary>Outcome of one publishing cycle (<see cref="OutboxBatchPublisher{TDbContext}.PublishPendingAsync"/>).</summary>
/// <param name="Skipped">Another instance held the publishing lock, so nothing was read.</param>
/// <param name="Published">How many rows were published and marked as such.</param>
/// <param name="Failure">Why the cycle stopped early, or <see langword="null"/> when every row read was published.</param>
/// <param name="MayHaveMore">The batch was full and succeeded, so more rows may be waiting.</param>
public sealed record OutboxBatchResult(bool Skipped, int Published, Exception? Failure, bool MayHaveMore)
{
    public static OutboxBatchResult LockNotAcquired { get; } = new(Skipped: true, Published: 0, Failure: null, MayHaveMore: false);
}
