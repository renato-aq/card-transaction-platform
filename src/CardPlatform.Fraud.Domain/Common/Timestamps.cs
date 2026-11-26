namespace CardPlatform.Fraud.Domain.Common;

/// <summary>
/// The precision of every instant the Fraud Worker records: whole microseconds, what a PostgreSQL <c>timestamptz</c>
/// keeps, so an instant read back from the database equals the one recorded.
/// </summary>
public static class Timestamps
{
    /// <summary><paramref name="value"/> without its sub-microsecond ticks (the offset is kept).</summary>
    public static DateTimeOffset TruncateToMicroseconds(this DateTimeOffset value)
    {
        return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
