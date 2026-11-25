namespace CardPlatform.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when another request changed one of the saved aggregates
/// since it was read (optimistic concurrency, ADR 0006). Nothing was saved. Adapters translate their own exceptions
/// into this one, so the Application layer does not depend on the persistence technology.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("Another request changed the data since it was read.")
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
