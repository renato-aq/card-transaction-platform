namespace CardPlatform.Ledger.Domain.Common;

/// <summary>
/// The precision of every instant the Ledger records: whole microseconds, what a PostgreSQL <c>timestamptz</c> keeps.
/// A clock may be finer (<see cref="TimeProvider.System"/> ticks every 100 ns on Linux), so an instant is truncated
/// when it is recorded; otherwise the value returned right after a write would differ from the one read back from the
/// database, e.g. by an idempotent repeat of the same hold (ADR 0007) or a later read of it.
/// </summary>
public static class Timestamps
{
    /// <summary><paramref name="value"/> without its sub-microsecond ticks (the offset is kept).</summary>
    public static DateTimeOffset TruncateToMicroseconds(this DateTimeOffset value)
    {
        return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
