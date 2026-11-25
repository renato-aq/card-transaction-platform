namespace CardPlatform.Ledger.Application.Concurrency;

/// <summary>
/// Waits before a retry of <see cref="RetryOnConcurrencyConflictDecorator{TCommand, TResult}"/>, so concurrent
/// requests that conflicted do not retry at the same instant.
/// </summary>
public interface IRetryDelay
{
    /// <param name="retry">The retry about to run, starting at 1.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    Task WaitAsync(int retry, CancellationToken cancellationToken = default);
}
