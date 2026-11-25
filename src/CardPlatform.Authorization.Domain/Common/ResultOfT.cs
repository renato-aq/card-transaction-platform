namespace CardPlatform.Authorization.Domain.Common;

/// <summary>
/// Outcome of an operation that produces a <typeparamref name="T"/> on success.
/// Create instances through <see cref="Result.Success{T}(T)"/> and <see cref="Result.Failure{T}(Error)"/>.
/// </summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, Error? error)
        : base(error)
    {
        _value = value;
    }

    /// <summary>
    /// The produced value. Throws <see cref="InvalidOperationException"/> when the result is a failure.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"A failed result has no value (error: {Error.Code}).");
}
