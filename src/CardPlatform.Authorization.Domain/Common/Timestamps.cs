namespace CardPlatform.Authorization.Domain.Common;

/// <summary>
/// The precision of every instant the Authorization records: whole microseconds, what a PostgreSQL <c>timestamptz</c>
/// keeps. A clock may be finer (<see cref="TimeProvider.System"/> ticks every 100 ns on Linux), so an instant is
/// truncated when it is recorded; otherwise the value returned right after a write (and stored byte for byte as an
/// idempotent response, ADR 0008) would differ from the one read back from the database.
/// </summary>
public static class Timestamps
{
    /// <summary><paramref name="value"/> without its sub-microsecond ticks (the offset is kept).</summary>
    public static DateTimeOffset TruncateToMicroseconds(this DateTimeOffset value)
    {
        return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
