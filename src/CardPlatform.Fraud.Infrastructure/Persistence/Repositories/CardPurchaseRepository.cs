using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Fraud.Domain.Rules;

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

    /// <remarks>
    /// Two aggregate queries instead of loading the history, both on the index <c>(card_id, authorized_at)</c>: a count
    /// over the velocity window (an index range) and the count and sum of the card's approved purchases in the
    /// purchase's currency up to it (the card's rows, filtered). Only numbers come back, never the purchases.
    /// </remarks>
    public async Task<CardActivity> GetActivityAsync(
        CardPurchase purchase,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        Guid cardId = purchase.CardId;
        Guid purchaseId = purchase.Id;
        DateTimeOffset at = purchase.AuthorizedAt;
        DateTimeOffset windowStart = at - window;
        string currency = purchase.Amount.Currency;

        // The approved (including captured or voided) purchases of the card up to this one, without it.
        IQueryable<CardPurchase> previous = dbContext.CardPurchases.Where(other =>
            other.CardId == cardId
            && other.Id != purchaseId
            && other.Status != PurchaseStatus.Declined
            && other.AuthorizedAt <= at);

        int recent = await previous
            .CountAsync(other => other.AuthorizedAt > windowStart, cancellationToken)
            .ConfigureAwait(false);

        var spending = await previous
            .Where(other => other.Amount.Currency == currency)
            .GroupBy(static _ => 1)
            .Select(static group => new { Count = group.Count(), Total = group.Sum(other => other.Amount.Amount) })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CardActivity(
            recent,
            spending is null ? SpendingSummary.None : new SpendingSummary(spending.Count, spending.Total));
    }

    public void Add(CardPurchase purchase)
    {
        dbContext.CardPurchases.Add(purchase);
    }
}
