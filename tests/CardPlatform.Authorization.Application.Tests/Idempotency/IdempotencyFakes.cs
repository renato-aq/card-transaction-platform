using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Idempotency;
using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Tests.Idempotency;

/// <summary>
/// In-memory <see cref="IIdempotencyKeyStore"/> with the semantics of the database adapter: a key is claimed when it
/// is new, or has no response, the same hash and no live claim.
/// </summary>
internal sealed class FakeIdempotencyKeyStore : IIdempotencyKeyStore
{
    public Dictionary<string, StoredKey> Keys { get; } = new(StringComparer.Ordinal);

    public List<(string Key, StoredResponse Response)> Completed { get; } = [];

    public List<string> Released { get; } = [];

    public List<string> Forgotten { get; } = [];

    public int ClaimAttempts { get; private set; }

    public Task<IdempotencyKeyEntry?> TryClaimAsync(IdempotencyClaim claim, CancellationToken cancellationToken = default)
    {
        ClaimAttempts++;
        if (!Keys.TryGetValue(claim.Key, out StoredKey? key))
        {
            key = new StoredKey(claim.RequestHash, claim.AuthorizationId);
            Keys[claim.Key] = key;
        }
        else if (key.Response is not null
            || key.RequestHash != claim.RequestHash
            || (key.LockedUntil is { } lockedUntil && lockedUntil > claim.ClaimedAt))
        {
            return Task.FromResult<IdempotencyKeyEntry?>(null);
        }

        key.ClaimToken = claim.ClaimToken;
        key.LockedUntil = claim.LockedUntil;
        return Task.FromResult<IdempotencyKeyEntry?>(ToEntry(claim.Key, key));
    }

    public Task<IdempotencyKeyEntry?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Keys.TryGetValue(key, out StoredKey? stored) ? ToEntry(key, stored) : null);
    }

    public void Complete(string key, Guid claimToken, StoredResponse response, DateTimeOffset completedAt)
    {
        Completed.Add((key, response));
        StoredKey stored = Keys[key];
        stored.Response = response;
        stored.ClaimToken = null;
        stored.LockedUntil = null;
    }

    public Task ReleaseAsync(string key, Guid claimToken, CancellationToken cancellationToken = default)
    {
        Released.Add(key);
        Keys[key].ClaimToken = null;
        Keys[key].LockedUntil = null;
        return Task.CompletedTask;
    }

    public Task ForgetAsync(string key, Guid claimToken, CancellationToken cancellationToken = default)
    {
        Forgotten.Add(key);
        Keys.Remove(key);
        return Task.CompletedTask;
    }

    private static IdempotencyKeyEntry ToEntry(string key, StoredKey stored)
    {
        return new IdempotencyKeyEntry(key, stored.RequestHash, stored.AuthorizationId, stored.Response);
    }

    internal sealed class StoredKey(string requestHash, Guid authorizationId)
    {
        public string RequestHash { get; } = requestHash;

        public Guid AuthorizationId { get; } = authorizationId;

        public Guid? ClaimToken { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }

        public StoredResponse? Response { get; set; }
    }
}

/// <summary>
/// <see cref="AuthorizePurchase"/> handler that records the commands it receives and answers <see cref="Result"/> or
/// throws <see cref="Exception"/>; by default it approves with the given authorization id.
/// </summary>
internal sealed class FakeAuthorizePurchaseHandler : ICommandHandler<AuthorizePurchase, AuthorizationDto>
{
    public List<AuthorizePurchase> Received { get; } = [];

    public Result<AuthorizationDto>? Result { get; set; }

    public Exception? Exception { get; set; }

    public Task<Result<AuthorizationDto>> HandleAsync(AuthorizePurchase command, CancellationToken cancellationToken = default)
    {
        Received.Add(command);
        if (Exception is not null)
        {
            throw Exception;
        }

        return Task.FromResult(Result ?? Domain.Common.Result.Success(new AuthorizationDto(
            command.AuthorizationId!.Value,
            Domain.Authorizations.AuthorizationStatus.Approved,
            null,
            command.CardId!.Value,
            command.MerchantAccountId!.Value,
            command.Amount!.Value,
            command.Currency!,
            DateTimeOffset.UnixEpoch)));
    }
}

/// <summary>Stores <c>201</c> with the authorization id as the body.</summary>
internal sealed class FakeResponseFactory : IAuthorizationResponseFactory
{
    public StoredResponse Create(AuthorizationDto authorization)
    {
        return new StoredResponse(authorization.Id, 201, authorization.Id.ToString());
    }
}

/// <summary>Unit of work that counts saves, runs <see cref="OnSave"/> and then throws <see cref="Exception"/> if set.</summary>
internal sealed class ThrowingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Action? OnSave { get; set; }

    public Exception? Exception { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        OnSave?.Invoke();
        return Exception is null ? Task.CompletedTask : Task.FromException(Exception);
    }
}
