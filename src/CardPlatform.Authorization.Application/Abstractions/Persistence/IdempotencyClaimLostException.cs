namespace CardPlatform.Authorization.Application.Abstractions.Persistence;

/// <summary>
/// Thrown by <see cref="IUnitOfWork.SaveChangesAsync"/> when completing an idempotency key whose claim was taken over
/// by another request (its lease expired), or when that request already recorded the authorization. Nothing was saved.
/// </summary>
public sealed class IdempotencyClaimLostException : Exception
{
    public IdempotencyClaimLostException()
    {
    }

    public IdempotencyClaimLostException(string message)
        : base(message)
    {
    }

    public IdempotencyClaimLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
