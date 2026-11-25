using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Tests.Cards;

/// <summary>In-memory adapters shared by the card handler tests.</summary>
internal sealed class FakeCardRepository(params Card[] existing) : ICardRepository
{
    private readonly List<Card> _cards = [.. existing];

    public List<Card> Added { get; } = [];

    public Task<Card?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cards.Find(card => card.Id == id));
    }

    public void Add(Card card)
    {
        Added.Add(card);
        _cards.Add(card);
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

/// <summary>Ledger whose <see cref="OpenCardAccountAsync"/> answers <see cref="OpenCardAccountResult"/>.</summary>
internal sealed class FakeLedgerClient : ILedgerClient
{
    public Result<LedgerAccount>? OpenCardAccountResult { get; set; }

    public List<string> OpenedCurrencies { get; } = [];

    public Task<Result<LedgerAccount>> OpenCardAccountAsync(string currency, CancellationToken cancellationToken = default)
    {
        OpenedCurrencies.Add(currency);
        return Task.FromResult(OpenCardAccountResult ?? Result.Success(new LedgerAccount(Guid.NewGuid(), currency, 0m)));
    }

    public Task<Result<LedgerBalance>> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
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

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}
