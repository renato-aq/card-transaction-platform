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

        return ExponentialBackoff.For(consecutiveFailures, options.RetryInitialDelay, options.RetryMaxDelay);
    }
}
