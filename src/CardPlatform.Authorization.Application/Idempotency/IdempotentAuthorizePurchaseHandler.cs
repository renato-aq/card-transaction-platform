using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Idempotency;

/// <summary>
/// Handles <see cref="IdempotentAuthorizePurchase"/> by wrapping the <see cref="AuthorizePurchase"/> handler with the
/// idempotency of ADR 0008:
/// <list type="number">
/// <item>a missing or blank key is <c>idempotency_key_required</c>; a key longer than <see cref="MaxKeyLength"/> is
/// <c>invalid_idempotency_key</c>;</item>
/// <item>the key is claimed (<see cref="IIdempotencyKeyStore.TryClaimAsync"/>) with the hash of the request
/// (<see cref="PurchaseRequestHash"/>), a new authorization id and a lease of <see cref="LeaseDuration"/>. When it cannot
/// be claimed nothing is processed and the Ledger is not called: a different hash is <c>idempotency_key_reused</c>, a
/// stored response is returned as it is, otherwise another request holds the claim
/// (<c>idempotency_request_in_progress</c>);</item>
/// <item>the purchase is authorized with the authorization id reserved for the key, the same one on every attempt, so a
/// hold placed by an attempt whose answer was lost is found again in the Ledger instead of placed twice;</item>
/// <item>an authorization (approved or declined) is saved together with its response on the key, in one transaction,
/// and the response is returned.</item>
/// </list>
/// Failures are not stored. <see cref="ErrorType.Validation"/> and <see cref="ErrorType.NotFound"/> failures reject the
/// request before any hold can exist, so the key is deleted and can be used again. Any other failure (e.g.
/// <c>ledger_unavailable</c>, where the hold may have been placed) or exception only ends the claim: the key keeps its
/// hash and authorization id, and the next request with it processes the purchase again with that id.
/// </summary>
public sealed class IdempotentAuthorizePurchaseHandler(
    ICommandHandler<AuthorizePurchase, AuthorizationDto> inner,
    IIdempotencyKeyStore keys,
    IUnitOfWork unitOfWork,
    IAuthorizationResponseFactory responses,
    TimeProvider timeProvider) : ICommandHandler<IdempotentAuthorizePurchase, StoredResponse>
{
    /// <summary>Maximum length of an idempotency key.</summary>
    public const int MaxKeyLength = 255;

    /// <summary>
    /// How long a claim lasts: after it, a request that crashed or hung no longer blocks its key. Much longer than the
    /// time budget of a purchase (the Ledger client gives up after a few seconds).
    /// </summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    public async Task<Result<StoredResponse>> HandleAsync(
        IdempotentAuthorizePurchase command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Purchase);

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return Result.Failure<StoredResponse>(IdempotencyErrors.KeyRequired);
        }

        if (command.IdempotencyKey.Length > MaxKeyLength)
        {
            return Result.Failure<StoredResponse>(IdempotencyErrors.InvalidKey);
        }

        string requestHash = PurchaseRequestHash.Compute(command.Purchase);
        // Truncated like every instant the Authorization records (see Timestamps), so the claim and lease written to
        // idempotency_keys are exactly the values PostgreSQL keeps.
        DateTimeOffset now = timeProvider.GetUtcNow().TruncateToMicroseconds();
        IdempotencyClaim claim = new(
            command.IdempotencyKey,
            requestHash,
            CardAuthorization.NewId(now),
            Guid.NewGuid(),
            now,
            now + LeaseDuration);

        IdempotencyKeyEntry? claimed = await keys.TryClaimAsync(claim, cancellationToken).ConfigureAwait(false);
        if (claimed is null)
        {
            return await ResolveUnclaimedAsync(claim, cancellationToken).ConfigureAwait(false);
        }

        return await ProcessAsync(claim, claimed.AuthorizationId, command.Purchase, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<StoredResponse>> ProcessAsync(
        IdempotencyClaim claim,
        Guid authorizationId,
        AuthorizePurchase purchase,
        CancellationToken cancellationToken)
    {
        StoredResponse response;
        try
        {
            Result<AuthorizationDto> result = await inner
                .HandleAsync(purchase with { AuthorizationId = authorizationId }, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await EndClaimAsync(claim, result.Error).ConfigureAwait(false);
                return Result.Failure<StoredResponse>(result.Error);
            }

            response = responses.Create(result.Value);
            keys.Complete(claim.Key, claim.ClaimToken, response, timeProvider.GetUtcNow().TruncateToMicroseconds());
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IdempotencyClaimLostException)
        {
            // Another request took the claim over (this one outlived its lease) and may already have answered.
            return await ResolveUnclaimedAsync(claim, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Unexpected failure: free the key now instead of at the end of the lease. Not cancellable, so it also
            // runs when the request was cancelled.
            await keys.ReleaseAsync(claim.Key, claim.ClaimToken, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return Result.Success(response);
    }

    private async Task EndClaimAsync(IdempotencyClaim claim, Error error)
    {
        if (error.Type is ErrorType.Validation or ErrorType.NotFound)
        {
            await keys.ForgetAsync(claim.Key, claim.ClaimToken, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            await keys.ReleaseAsync(claim.Key, claim.ClaimToken, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<Result<StoredResponse>> ResolveUnclaimedAsync(
        IdempotencyClaim claim,
        CancellationToken cancellationToken)
    {
        IdempotencyKeyEntry? entry = await keys.FindAsync(claim.Key, cancellationToken).ConfigureAwait(false);

        if (entry is not null && !string.Equals(entry.RequestHash, claim.RequestHash, StringComparison.Ordinal))
        {
            return Result.Failure<StoredResponse>(IdempotencyErrors.KeyReused);
        }

        if (entry?.Response is { } response)
        {
            return Result.Success(response);
        }

        // Still claimed by another request (or freed by it in the meantime): the client tries again later.
        return Result.Failure<StoredResponse>(IdempotencyErrors.RequestInProgress);
    }
}
