using CardPlatform.Authorization.Application.Idempotency;

namespace CardPlatform.Authorization.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port for the idempotency keys of <c>POST /authorizations</c> (ADR 0008). A key is unique (database
/// constraint); it keeps the hash of the first request, the authorization id reserved for it and, once processed, the
/// response. While a request is processing it, the key is claimed by that request's token until a lease expires.
/// </summary>
public interface IIdempotencyKeyStore
{
    /// <summary>
    /// Claims <see cref="IdempotencyClaim.Key"/> in its own short transaction and returns it, or returns
    /// <see langword="null"/> when it cannot be claimed. A new key is inserted with the hash and the authorization id of
    /// <paramref name="claim"/>. An existing key is claimed only when it has no response, the same request hash and no
    /// live claim (released or lease expired); it then keeps the authorization id reserved before, which the returned
    /// entry carries.
    /// </summary>
    Task<IdempotencyKeyEntry?> TryClaimAsync(IdempotencyClaim claim, CancellationToken cancellationToken = default);

    /// <summary>Reads a key without claiming it; <see langword="null"/> when it does not exist.</summary>
    Task<IdempotencyKeyEntry?> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records <paramref name="response"/> on a key claimed with <paramref name="claimToken"/> and ends the claim. Like
    /// the repositories, it is saved by the next <see cref="IUnitOfWork.SaveChangesAsync"/>, in the same transaction as
    /// the authorization; that save throws <see cref="IdempotencyClaimLostException"/> when the claim was taken over.
    /// </summary>
    void Complete(string key, Guid claimToken, StoredResponse response, DateTimeOffset completedAt);

    /// <summary>
    /// Ends the claim of <paramref name="claimToken"/> right away, keeping the key, its hash and its reserved
    /// authorization id, so the next request with the key processes it again with the same id. Does nothing when the
    /// claim was taken over.
    /// </summary>
    Task ReleaseAsync(string key, Guid claimToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a key claimed with <paramref name="claimToken"/> right away, so it can be used again for any request.
    /// Does nothing when the claim was taken over.
    /// </summary>
    Task ForgetAsync(string key, Guid claimToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// A request to claim <paramref name="Key"/> for a request whose hash is <paramref name="RequestHash"/>: a new key gets
/// <paramref name="AuthorizationId"/>; the claim is identified by <paramref name="ClaimToken"/> and lasts until
/// <paramref name="LockedUntil"/>.
/// </summary>
public sealed record IdempotencyClaim(
    string Key,
    string RequestHash,
    Guid AuthorizationId,
    Guid ClaimToken,
    DateTimeOffset ClaimedAt,
    DateTimeOffset LockedUntil);

/// <summary>
/// A stored idempotency key: the hash of its request, its reserved authorization id and its response, which is
/// <see langword="null"/> until the request is processed.
/// </summary>
public sealed record IdempotencyKeyEntry(string Key, string RequestHash, Guid AuthorizationId, StoredResponse? Response);
