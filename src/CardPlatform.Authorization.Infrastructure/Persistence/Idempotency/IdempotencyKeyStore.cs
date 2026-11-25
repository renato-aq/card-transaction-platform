using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Application.Idempotency;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CardPlatform.Authorization.Infrastructure.Persistence.Idempotency;

/// <summary>
/// EF Core adapter of <see cref="IIdempotencyKeyStore"/> on <c>idempotency_keys</c>. Claims, releases and deletes run as
/// single statements outside the change tracker (each one is its own transaction); a completion is tracked and saved
/// with the authorization by <see cref="AuthorizationDbContext"/>.
/// </summary>
internal sealed class IdempotencyKeyStore(AuthorizationDbContext dbContext) : IIdempotencyKeyStore
{
    public async Task<IdempotencyKeyEntry?> TryClaimAsync(
        IdempotencyClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);

        DateTimeOffset claimedAt = claim.ClaimedAt.ToUniversalTime();
        DateTimeOffset lockedUntil = claim.LockedUntil.ToUniversalTime();

        // The primary key makes the key unique: of several requests inserting the same key at once, PostgreSQL lets one
        // insert it and makes the others wait for that commit and take the ON CONFLICT path. An existing key is
        // claimed only when it has no response, the same request hash and no live claim; the update keeps its reserved
        // authorization id. No row comes back when the key could not be claimed.
        List<IdempotencyKeyRecord> claimed = await dbContext.IdempotencyKeys
            .FromSql($"""
                INSERT INTO idempotency_keys (key, request_hash, authorization_id, claim_token, locked_until, created_at)
                VALUES ({claim.Key}, {claim.RequestHash}, {claim.AuthorizationId}, {claim.ClaimToken}, {lockedUntil}, {claimedAt})
                ON CONFLICT (key) DO UPDATE
                    SET claim_token = excluded.claim_token, locked_until = excluded.locked_until
                    WHERE idempotency_keys.response_status_code IS NULL
                        AND idempotency_keys.request_hash = excluded.request_hash
                        AND (idempotency_keys.locked_until IS NULL OR idempotency_keys.locked_until <= excluded.created_at)
                RETURNING *
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return claimed.Count == 0 ? null : ToEntry(claimed[0]);
    }

    public async Task<IdempotencyKeyEntry?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        IdempotencyKeyRecord? record = await dbContext.IdempotencyKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Key == key, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : ToEntry(record);
    }

    public void Complete(string key, Guid claimToken, StoredResponse response, DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Only the columns set here are updated; the claim token's original value is the concurrency check.
        IdempotencyKeyRecord record = new() { Key = key, RequestHash = string.Empty, ClaimToken = claimToken };
        EntityEntry<IdempotencyKeyRecord> entry = dbContext.IdempotencyKeys.Attach(record);

        record.ClaimToken = null;
        record.LockedUntil = null;
        record.ResponseStatusCode = response.StatusCode;
        record.ResponseBody = response.Body;
        record.CompletedAt = completedAt;

        entry.Property(entity => entity.ClaimToken).IsModified = true;
        entry.Property(entity => entity.LockedUntil).IsModified = true;
        entry.Property(entity => entity.ResponseStatusCode).IsModified = true;
        entry.Property(entity => entity.ResponseBody).IsModified = true;
        entry.Property(entity => entity.CompletedAt).IsModified = true;
    }

    public async Task ReleaseAsync(string key, Guid claimToken, CancellationToken cancellationToken = default)
    {
        await dbContext.IdempotencyKeys
            .Where(record => record.Key == key && record.ClaimToken == claimToken)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(record => record.ClaimToken, (Guid?)null)
                    .SetProperty(record => record.LockedUntil, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ForgetAsync(string key, Guid claimToken, CancellationToken cancellationToken = default)
    {
        await dbContext.IdempotencyKeys
            .Where(record => record.Key == key && record.ClaimToken == claimToken)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static IdempotencyKeyEntry ToEntry(IdempotencyKeyRecord record)
    {
        StoredResponse? response = record is { ResponseStatusCode: { } statusCode, ResponseBody: { } body }
            ? new StoredResponse(record.AuthorizationId, statusCode, body)
            : null;

        return new IdempotencyKeyEntry(record.Key, record.RequestHash, record.AuthorizationId, response);
    }
}
