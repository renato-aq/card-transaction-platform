using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Common;

namespace CardPlatform.Ledger.Application.Concurrency;

/// <summary>
/// Retries a command when saving it hits an optimistic concurrency conflict (ADR 0006). Before each retry the unit
/// of work discards what the failed attempt tracked and waits a short random delay, so the inner handler reloads the
/// accounts and re-applies its rules to their current state. After <see cref="MaxRetries"/> retries it returns
/// <c>concurrency_conflict</c>. Business failures and other exceptions are returned or thrown as they are, without
/// retrying.
/// </summary>
public sealed class RetryOnConcurrencyConflictDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IUnitOfWork unitOfWork,
    IRetryDelay retryDelay) : ICommandHandler<TCommand, TResult>
{
    /// <summary>Retries after the first attempt, so a command runs at most <c>MaxRetries + 1</c> times.</summary>
    public const int MaxRetries = 3;

    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await inner.HandleAsync(command, cancellationToken).ConfigureAwait(false);
            }
            catch (ConcurrencyConflictException)
            {
                unitOfWork.DiscardChanges();
                if (attempt > MaxRetries)
                {
                    return Result.Failure<TResult>(ConcurrencyErrors.ConcurrencyConflict);
                }
            }

            // The retry about to run has the number of the attempt that just failed.
            await retryDelay.WaitAsync(attempt, cancellationToken).ConfigureAwait(false);
        }
    }
}
