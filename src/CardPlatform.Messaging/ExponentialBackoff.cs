namespace CardPlatform.Messaging;

/// <summary>Bounded exponential backoff shared by the background services that retry after failures.</summary>
internal static class ExponentialBackoff
{
    /// <summary>
    /// The wait after <paramref name="consecutiveFailures"/> failures in a row: <paramref name="initialDelay"/> ×
    /// 2^(failures − 1), at most <paramref name="maxDelay"/>.
    /// </summary>
    public static TimeSpan For(int consecutiveFailures, TimeSpan initialDelay, TimeSpan maxDelay)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consecutiveFailures);

        // 2^20 times any sensible initial delay is far beyond the maximum; capping the exponent avoids overflow.
        double factor = Math.Pow(2, Math.Min(consecutiveFailures - 1, 20));
        double ticks = Math.Min(initialDelay.Ticks * factor, maxDelay.Ticks);

        return TimeSpan.FromTicks((long)ticks);
    }
}
