namespace CardPlatform.Ledger.Domain.Common;

/// <summary>
/// Outcome of an operation that can fail for an expected business reason.
/// Unexpected failures and programming errors are signalled with exceptions instead.
/// </summary>
public class Result
{
    private readonly Error? _error;

    protected Result(Error? error)
    {
        _error = error;
    }

    public bool IsSuccess => _error is null;

    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The failure reason. Throws <see cref="InvalidOperationException"/> when the result is a success.
    /// </summary>
    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success()
    {
        return new Result(null);
    }

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result<T> Success<T>(T value)
    {
        return new Result<T>(value, null);
    }

    public static Result<T> Failure<T>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }
}
