using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Purchases;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Fraud.Infrastructure.Persistence.Repositories;

internal sealed class CardPurchaseRepository(FraudDbContext dbContext) : ICardPurchaseRepository
{
    public Task<CardPurchase?> GetByIdAsync(Guid authorizationId, CancellationToken cancellationToken = default)
    {
        return dbContext.CardPurchases.SingleOrDefaultAsync(purchase => purchase.Id == authorizationId, cancellationToken);
    }

    public async Task<CardHistory> GetHistoryAsync(Guid cardId, CancellationToken cancellationToken = default)
    {
        List<CardPurchase> purchases = await dbContext.CardPurchases
            .Where(purchase => purchase.CardId == cardId)
            .OrderBy(purchase => purchase.AuthorizedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CardHistory(cardId, purchases);
    }

    public void Add(CardPurchase purchase)
    {
        dbContext.CardPurchases.Add(purchase);
    }
}
