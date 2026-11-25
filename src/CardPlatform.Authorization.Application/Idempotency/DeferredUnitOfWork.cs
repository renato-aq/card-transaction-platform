using CardPlatform.Authorization.Application.Abstractions.Persistence;

namespace CardPlatform.Authorization.Application.Idempotency;

/// <summary>
/// Unit of work given to the handler wrapped by <see cref="IdempotentAuthorizePurchaseHandler"/>: it saves nothing,
/// so the authorization the wrapped handler adds stays tracked until the idempotent handler has also tracked the
/// completion of the key and saves both with the real unit of work, in one database transaction.
/// </summary>
internal sealed class DeferredUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
