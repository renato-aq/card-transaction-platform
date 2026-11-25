using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Concurrency;
using CardPlatform.Ledger.Domain.Common;

namespace CardPlatform.Ledger.Application.Tests.Concurrency;

public class RetryOnConcurrencyConflictDecoratorTests
{
    private const string Command = "command";

    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeRetryDelay _retryDelay = new();

    [Fact]
    public async Task Persistent_conflicts_run_four_attempts_and_return_concurrency_conflict()
    {
        ScriptedHandler inner = new(_ => throw new ConcurrencyConflictException());

        Result<int> result = await Decorate(inner).HandleAsync(Command);

        Assert.True(result.IsFailure);
        Assert.Equal(ConcurrencyErrors.ConcurrencyConflictCode, result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(4, inner.Calls);
        Assert.Equal(RetryOnConcurrencyConflictDecorator<string, int>.MaxRetries + 1, inner.Calls);
        Assert.Equal([1, 2, 3], _retryDelay.Retries);
        Assert.Equal(4, _unitOfWork.DiscardCount);
    }

    [Fact]
    public async Task Conflict_then_success_returns_the_result_of_the_successful_attempt()
    {
        ScriptedHandler inner = new(attempt => attempt < 3
            ? throw new ConcurrencyConflictException()
            : Result.Success(attempt));

        Result<int> result = await Decorate(inner).HandleAsync(Command);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        Assert.Equal(3, inner.Calls);
        Assert.Equal([1, 2], _retryDelay.Retries);
        Assert.Equal(2, _unitOfWork.DiscardCount);
    }

    [Fact]
    public async Task Success_on_first_attempt_does_not_retry()
    {
        ScriptedHandler inner = new(Result.Success);

        Result<int> result = await Decorate(inner).HandleAsync(Command);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(_retryDelay.Retries);
        Assert.Equal(0, _unitOfWork.DiscardCount);
    }

    [Fact]
    public async Task Business_failure_is_returned_without_retrying()
    {
        Error insufficientFunds = Error.BusinessRule("insufficient_funds", "Not enough money.");
        ScriptedHandler inner = new(_ => Result.Failure<int>(insufficientFunds));

        Result<int> result = await Decorate(inner).HandleAsync(Command);

        Assert.True(result.IsFailure);
        Assert.Equal(insufficientFunds, result.Error);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(_retryDelay.Retries);
        Assert.Equal(0, _unitOfWork.DiscardCount);
    }

    [Fact]
    public async Task Unexpected_exception_is_not_retried()
    {
        ScriptedHandler inner = new(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Decorate(inner).HandleAsync(Command));

        Assert.Equal(1, inner.Calls);
        Assert.Empty(_retryDelay.Retries);
    }

    [Fact]
    public async Task Cancellation_during_the_delay_stops_retrying()
    {
        using CancellationTokenSource cancellation = new();
        ScriptedHandler inner = new(_ =>
        {
            cancellation.Cancel();
            throw new ConcurrencyConflictException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Decorate(inner).HandleAsync(Command, cancellation.Token));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Jittered_delay_waits_at_most_the_exponential_cap()
    {
        JitteredRetryDelay delay = new(TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => delay.WaitAsync(0));
        await delay.WaitAsync(1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => delay.WaitAsync(3, new CancellationToken(canceled: true)));
    }

    private RetryOnConcurrencyConflictDecorator<string, int> Decorate(ICommandHandler<string, int> inner)
    {
        return new RetryOnConcurrencyConflictDecorator<string, int>(inner, _unitOfWork, _retryDelay);
    }

    /// <summary>Runs <paramref name="attempt"/> with the 1-based number of each call.</summary>
    private sealed class ScriptedHandler(Func<int, Result<int>> attempt) : ICommandHandler<string, int>
    {
        public int Calls { get; private set; }

        public Task<Result<int>> HandleAsync(string command, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(attempt(Calls));
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int DiscardCount { get; private set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void DiscardChanges()
        {
            DiscardCount++;
        }
    }

    /// <summary>Records the retries without waiting, so the tests are fast and deterministic.</summary>
    private sealed class FakeRetryDelay : IRetryDelay
    {
        public List<int> Retries { get; } = [];

        public Task WaitAsync(int retry, CancellationToken cancellationToken = default)
        {
            Retries.Add(retry);
            return Task.Delay(TimeSpan.Zero, cancellationToken);
        }
    }
}
