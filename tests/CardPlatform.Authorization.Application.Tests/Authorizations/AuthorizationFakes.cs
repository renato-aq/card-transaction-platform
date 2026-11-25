using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Tests.Authorizations;

internal sealed class FakeAuthorizationRepository : IAuthorizationRepository
{
    public List<CardAuthorization> Added { get; } = [];

    public void Add(CardAuthorization authorization)
    {
        Added.Add(authorization);
    }
}

/// <summary>A hold request the <see cref="FakeHoldLedgerClient"/> received.</summary>
internal sealed record PlacedHold(Guid HoldId, Guid CardAccountId, decimal Amount, string Currency);

/// <summary>
/// Ledger whose <see cref="PlaceHoldAsync"/> answers <see cref="PlaceHoldError"/> when it is set, otherwise a pending
/// hold with the requested data.
/// </summary>
internal sealed class FakeHoldLedgerClient : ILedgerClient
{
    public Error? PlaceHoldError { get; set; }

    public List<PlacedHold> PlacedHolds { get; } = [];

    public Task<Result<LedgerHold>> PlaceHoldAsync(
        Guid holdId,
        Guid cardAccountId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        PlacedHolds.Add(new PlacedHold(holdId, cardAccountId, amount, currency));
        return Task.FromResult(PlaceHoldError is { } error
            ? Result.Failure<LedgerHold>(error)
            : Result.Success(new LedgerHold(
                holdId,
                cardAccountId,
                amount,
                currency,
                LedgerHoldStatus.Pending,
                DateTimeOffset.UnixEpoch,
                CompletedAt: null)));
    }

    public Task<Result<LedgerAccount>> OpenCardAccountAsync(string currency, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<Result<LedgerBalance>> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<Result<LedgerHold>> CaptureHoldAsync(
        Guid holdId,
        Guid merchantAccountId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<Result<LedgerHold>> VoidHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<Result<LedgerHold>> GetHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }
}
