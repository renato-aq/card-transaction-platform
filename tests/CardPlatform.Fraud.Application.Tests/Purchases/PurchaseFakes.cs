using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Purchases;

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

    public void Add(CardPurchase purchase)
    {
        Added.Add(purchase);
    }
}

internal sealed class FakeUnitOfWork(FakeCardPurchaseRepository purchases) : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        purchases.SaveAdded();
        return Task.CompletedTask;
    }
}
