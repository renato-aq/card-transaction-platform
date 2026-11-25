using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Contracts;

namespace CardPlatform.Authorization.Application.Tests.Authorizations;

internal sealed class FakeAuthorizationRepository(params CardAuthorization[] existing) : IAuthorizationRepository
{
    private readonly List<CardAuthorization> _authorizations = [.. existing];

    public List<CardAuthorization> Added { get; } = [];

    public Task<CardAuthorization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_authorizations.Find(authorization => authorization.Id == id));
    }

    public void Add(CardAuthorization authorization)
    {
        Added.Add(authorization);
        _authorizations.Add(authorization);
    }
}

/// <summary>An event added to the <see cref="FakeOutbox"/>.</summary>
internal sealed record OutboxEntry(IIntegrationEvent Event, Guid EventId, DateTimeOffset OccurredAt);

/// <summary>Outbox that records the events added to it.</summary>
internal sealed class FakeOutbox : IOutbox
{
    public List<OutboxEntry> Added { get; } = [];

    public void Add(IIntegrationEvent integrationEvent, Guid eventId, DateTimeOffset occurredAt)
    {
        Added.Add(new OutboxEntry(integrationEvent, eventId, occurredAt));
    }
}

/// <summary>
/// Unit of work that counts saves and records how many events the <see cref="FakeOutbox"/> held at each one: an event
/// counted there was added before the save, so the real adapter would insert it in that save's transaction.
/// </summary>
internal sealed class OutboxUnitOfWork(FakeOutbox outbox) : IUnitOfWork
{
    public int SaveCount => EventsAtSave.Count;

    public List<int> EventsAtSave { get; } = [];

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EventsAtSave.Add(outbox.Added.Count);
        return Task.CompletedTask;
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

/// <summary>
/// Ledger for capture and void: <see cref="CaptureHoldAsync"/>, <see cref="VoidHoldAsync"/> and
/// <see cref="GetHoldAsync"/> answer the configured error, or a hold in <see cref="HoldStatus"/> (captured or voided by
/// the call). Every call is recorded in <see cref="Calls"/>.
/// </summary>
internal sealed class FakeCompletionLedgerClient : ILedgerClient
{
    public Error? CaptureError { get; set; }

    public Error? VoidError { get; set; }

    public Error? GetHoldError { get; set; }

    /// <summary>State the hold is in for <see cref="GetHoldAsync"/>.</summary>
    public LedgerHoldStatus HoldStatus { get; set; } = LedgerHoldStatus.Pending;

    public List<string> Calls { get; } = [];

    public Guid? CapturedTo { get; private set; }

    public Task<Result<LedgerHold>> CaptureHoldAsync(
        Guid holdId,
        Guid merchantAccountId,
        CancellationToken cancellationToken = default)
    {
        Calls.Add($"capture {holdId}");
        CapturedTo = merchantAccountId;
        return Task.FromResult(Answer(holdId, CaptureError, LedgerHoldStatus.Captured));
    }

    public Task<Result<LedgerHold>> VoidHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"void {holdId}");
        return Task.FromResult(Answer(holdId, VoidError, LedgerHoldStatus.Voided));
    }

    public Task<Result<LedgerHold>> GetHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"get {holdId}");
        return Task.FromResult(Answer(holdId, GetHoldError, HoldStatus));
    }

    public Task<Result<LedgerHold>> PlaceHoldAsync(
        Guid holdId,
        Guid cardAccountId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<Result<LedgerAccount>> OpenCardAccountAsync(string currency, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public Task<Result<LedgerBalance>> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    private static Result<LedgerHold> Answer(Guid holdId, Error? error, LedgerHoldStatus status)
    {
        return error is not null
            ? Result.Failure<LedgerHold>(error)
            : Result.Success(new LedgerHold(
                holdId,
                Guid.NewGuid(),
                10m,
                "BRL",
                status,
                DateTimeOffset.UnixEpoch,
                status == LedgerHoldStatus.Pending ? null : DateTimeOffset.UnixEpoch));
    }
}
