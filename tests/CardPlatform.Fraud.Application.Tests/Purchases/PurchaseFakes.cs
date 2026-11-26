using CardPlatform.Contracts;
using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Fraud.Domain.Rules;

namespace CardPlatform.Fraud.Application.Tests.Purchases;

/// <summary>An in-memory purchase store; <see cref="Add"/> tracks a purchase, saved by <see cref="FakeUnitOfWork"/>.</summary>
internal sealed class FakeCardPurchaseRepository : ICardPurchaseRepository
{
    private readonly Dictionary<Guid, CardPurchase> _saved = [];

    public List<CardPurchase> Added { get; } = [];

    public IReadOnlyCollection<CardPurchase> Saved => _saved.Values;

    public void Seed(CardPurchase purchase)
    {
        _saved[purchase.Id] = purchase;
    }

    public void SaveAdded()
    {
        foreach (CardPurchase purchase in Added)
        {
            _saved[purchase.Id] = purchase;
        }

        Added.Clear();
    }

    public Task<CardPurchase?> GetByIdAsync(Guid authorizationId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_saved.GetValueOrDefault(authorizationId));
    }

    public Task<CardHistory> GetHistoryAsync(Guid cardId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CardHistory(cardId, _saved.Values.Where(purchase => purchase.CardId == cardId)));
    }

    public async Task<CardActivity> GetActivityAsync(CardPurchase purchase, TimeSpan window, CancellationToken cancellationToken = default)
    {
        return CardActivity.Of(purchase, await GetHistoryAsync(purchase.CardId, cancellationToken), window);
    }

    public void Add(CardPurchase purchase)
    {
        Added.Add(purchase);
    }
}

/// <summary>An in-memory outbox; <see cref="Add"/> tracks an event, saved by <see cref="FakeUnitOfWork"/>.</summary>
internal sealed class FakeOutbox : IOutbox
{
    public List<(IIntegrationEvent Event, Guid EventId, DateTimeOffset OccurredAt)> Added { get; } = [];

    public List<(IIntegrationEvent Event, Guid EventId, DateTimeOffset OccurredAt)> Saved { get; } = [];

    public void Add(IIntegrationEvent integrationEvent, Guid eventId, DateTimeOffset occurredAt)
    {
        Added.Add((integrationEvent, eventId, occurredAt));
    }

    public void SaveAdded()
    {
        Saved.AddRange(Added);
        Added.Clear();
    }
}

internal sealed class FakeUnitOfWork(FakeCardPurchaseRepository purchases, FakeOutbox? outbox = null) : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        purchases.SaveAdded();
        outbox?.SaveAdded();
        return Task.CompletedTask;
    }
}

/// <summary>A clock that always says <see cref="Now"/>.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; } = now;

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }
}
