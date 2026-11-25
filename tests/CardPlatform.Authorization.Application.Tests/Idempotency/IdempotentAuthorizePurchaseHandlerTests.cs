using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Idempotency;
using CardPlatform.Authorization.Application.Tests.Cards;
using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Tests.Idempotency;

public class IdempotentAuthorizePurchaseHandlerTests
{
    private const string Key = "key-1";

    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private readonly AuthorizePurchase _purchase = new(Guid.NewGuid(), Guid.NewGuid(), 30m, "BRL");
    private readonly FakeAuthorizePurchaseHandler _inner = new();
    private readonly FakeIdempotencyKeyStore _keys = new();
    private readonly ThrowingUnitOfWork _unitOfWork = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Missing_key_fails_with_idempotency_key_required_without_processing(string? key)
    {
        Result<StoredResponse> result = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(key, _purchase));

        Assert.Equal(IdempotencyErrors.KeyRequired, result.Error);
        Assert.Equal(0, _keys.ClaimAttempts);
        Assert.Empty(_inner.Received);
    }

    [Fact]
    public async Task Too_long_key_fails_with_invalid_idempotency_key()
    {
        string key = new('k', IdempotentAuthorizePurchaseHandler.MaxKeyLength + 1);

        Result<StoredResponse> result = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(key, _purchase));

        Assert.Equal(IdempotencyErrors.InvalidKey, result.Error);
        Assert.Empty(_inner.Received);
    }

    [Fact]
    public async Task New_key_is_processed_with_the_reserved_id_and_completed_in_the_same_save()
    {
        Result<StoredResponse> result = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Assert.True(result.IsSuccess);
        Guid reservedId = _keys.Keys[Key].AuthorizationId;
        Assert.Equal(7, reservedId.Version);
        AuthorizePurchase received = Assert.Single(_inner.Received);
        Assert.Equal(_purchase with { AuthorizationId = reservedId }, received);
        Assert.Equal(new StoredResponse(reservedId, 201, reservedId.ToString()), result.Value);
        Assert.Equal((Key, result.Value), Assert.Single(_keys.Completed));
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(PurchaseRequestHash.Compute(_purchase), _keys.Keys[Key].RequestHash);
    }

    [Fact]
    public async Task Claim_lease_and_completion_times_are_written_in_whole_microseconds()
    {
        // A Linux clock ticks every 100 ns; PostgreSQL keeps whole microseconds.
        Result<StoredResponse> result = await CreateHandler(new FixedTimeProvider(Now.AddTicks(7)))
            .HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Assert.True(result.IsSuccess);
        IdempotencyClaim claim = Assert.Single(_keys.Claims);
        Assert.Equal(Now, claim.ClaimedAt);
        Assert.Equal(Now + IdempotentAuthorizePurchaseHandler.LeaseDuration, claim.LockedUntil);
        Assert.Equal(Now, Assert.Single(_keys.CompletedAt));
    }

    [Fact]
    public async Task Repeated_key_returns_the_stored_response_without_processing_again()
    {
        Result<StoredResponse> first = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Result<StoredResponse> second = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Assert.Equal(first.Value, second.Value);
        Assert.Single(_inner.Received);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Key_used_with_another_request_fails_with_idempotency_key_reused()
    {
        await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Result<StoredResponse> result = await CreateHandler()
            .HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase with { Amount = 31m }));

        Assert.Equal(IdempotencyErrors.KeyReused, result.Error);
        Assert.Single(_inner.Received);
    }

    [Fact]
    public async Task Key_claimed_by_another_request_fails_with_idempotency_request_in_progress()
    {
        _keys.Keys[Key] = new FakeIdempotencyKeyStore.StoredKey(PurchaseRequestHash.Compute(_purchase), Guid.NewGuid())
        {
            ClaimToken = Guid.NewGuid(),
            LockedUntil = Now.AddSeconds(10),
        };

        Result<StoredResponse> result = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Assert.Equal(IdempotencyErrors.RequestInProgress, result.Error);
        Assert.Empty(_inner.Received);
    }

    [Fact]
    public async Task Released_key_is_processed_again_with_the_id_reserved_before()
    {
        Guid reservedId = Guid.CreateVersion7();
        _keys.Keys[Key] = new FakeIdempotencyKeyStore.StoredKey(PurchaseRequestHash.Compute(_purchase), reservedId);

        Result<StoredResponse> result = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Assert.Equal(reservedId, result.Value.AuthorizationId);
        Assert.Equal(reservedId, Assert.Single(_inner.Received).AuthorizationId);
    }

    [Fact]
    public async Task Unavailable_ledger_is_not_stored_and_keeps_the_reserved_id_for_the_retry()
    {
        _inner.Result = Result.Failure<AuthorizationDto>(LedgerErrors.Unavailable());

        Result<StoredResponse> failed = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Assert.Equal(LedgerErrors.UnavailableCode, failed.Error.Code);
        Assert.Equal([Key], _keys.Released);
        Assert.Empty(_keys.Completed);
        Assert.Equal(0, _unitOfWork.SaveCount);

        _inner.Result = null;
        Result<StoredResponse> retried = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Assert.True(retried.IsSuccess);
        Assert.Equal(2, _inner.Received.Count);
        Assert.Equal(_inner.Received[0].AuthorizationId, _inner.Received[1].AuthorizationId);
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.NotFound)]
    public async Task Rejected_request_deletes_the_key(ErrorType type)
    {
        _inner.Result = Result.Failure<AuthorizationDto>(new Error("rejected", "Rejected.", type));

        Result<StoredResponse> result = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Assert.Equal("rejected", result.Error.Code);
        Assert.Equal([Key], _keys.Forgotten);
        Assert.Empty(_keys.Keys);
    }

    [Fact]
    public async Task Unexpected_exception_releases_the_key_and_propagates()
    {
        _inner.Exception = new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase)));

        Assert.Equal([Key], _keys.Released);
        Assert.Empty(_keys.Completed);
    }

    [Fact]
    public async Task Claim_lost_while_saving_returns_the_response_recorded_by_the_other_request()
    {
        StoredResponse recorded = new(Guid.NewGuid(), 201, "recorded by the other request");
        _unitOfWork.OnSave = () => _keys.Keys[Key].Response = recorded;
        _unitOfWork.Exception = new IdempotencyClaimLostException("lost");

        Result<StoredResponse> result = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Assert.Equal(recorded, result.Value);
        Assert.Empty(_keys.Released);
    }

    [Fact]
    public async Task Claim_lost_while_the_other_request_is_still_running_fails_with_idempotency_request_in_progress()
    {
        _unitOfWork.OnSave = () =>
        {
            _keys.Keys[Key].Response = null;
            _keys.Keys[Key].ClaimToken = Guid.NewGuid();
            _keys.Keys[Key].LockedUntil = Now.AddSeconds(30);
        };
        _unitOfWork.Exception = new IdempotencyClaimLostException("lost");

        Result<StoredResponse> result = await CreateHandler().HandleAsync(new IdempotentAuthorizePurchase(Key, _purchase));

        Assert.Equal(IdempotencyErrors.RequestInProgress, result.Error);
    }

    private IdempotentAuthorizePurchaseHandler CreateHandler(TimeProvider? timeProvider = null)
    {
        return new IdempotentAuthorizePurchaseHandler(
            _inner,
            _keys,
            _unitOfWork,
            new FakeResponseFactory(),
            timeProvider ?? new FixedTimeProvider(Now));
    }
}
