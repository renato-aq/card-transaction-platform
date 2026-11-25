namespace CardPlatform.Ledger.Application.Concurrency;

/// <summary>
/// Exponential backoff with full jitter: retry <c>n</c> waits a random time between zero and
/// <see cref="BaseDelay"/> × 2<sup>n−1</sup>, so conflicting requests spread out instead of colliding again.
/// </summary>
public sealed class JitteredRetryDelay(TimeProvider timeProvider) : IRetryDelay
{
    public static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(50);

    public Task WaitAsync(int retry, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retry, 1);

        TimeSpan maxDelay = BaseDelay * Math.Pow(2, retry - 1);
        TimeSpan delay = maxDelay * Random.Shared.NextDouble();
        return Task.Delay(delay, timeProvider, cancellationToken);
    }
}
