namespace CardPlatform.Messaging.Publishing;

/// <summary>Bounded exponential backoff between failed publishing cycles.</summary>
public static class OutboxRetryDelay
{
    /// <summary>
    /// The wait after <paramref name="consecutiveFailures"/> failed cycles in a row:
    /// <see cref="OutboxPublisherOptions.RetryInitialDelay"/> × 2^(failures − 1), at most
    /// <see cref="OutboxPublisherOptions.RetryMaxDelay"/>.
    /// </summary>
    public static TimeSpan For(int consecutiveFailures, OutboxPublisherOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(consecutiveFailures);
        ArgumentNullException.ThrowIfNull(options);

        // 2^20 times any sensible initial delay is far beyond the maximum; capping the exponent avoids overflow.
        double factor = Math.Pow(2, Math.Min(consecutiveFailures - 1, 20));
        double ticks = Math.Min(options.RetryInitialDelay.Ticks * factor, options.RetryMaxDelay.Ticks);

        return TimeSpan.FromTicks((long)ticks);
    }
}
