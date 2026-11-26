using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Fraud.Domain.Rules;

namespace CardPlatform.Fraud.Application.Abstractions.Persistence;

/// <summary>Outbound port to the recorded purchases of the cards.</summary>
public interface ICardPurchaseRepository
{
    /// <summary>The purchase of the authorization <paramref name="authorizationId"/>, or <see langword="null"/>.</summary>
    Task<CardPurchase?> GetByIdAsync(Guid authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Every purchase recorded for <paramref name="cardId"/> (empty when there is none).</summary>
    Task<CardHistory> GetHistoryAsync(Guid cardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The activity of <paramref name="purchase"/>'s card before it, as <see cref="CardActivity.Of"/> defines it from
    /// the whole history (velocity window of length <paramref name="window"/>), without loading every purchase of the
    /// card. <paramref name="purchase"/> itself is never counted, whether it was saved already or not.
    /// </summary>
    Task<CardActivity> GetActivityAsync(CardPurchase purchase, TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>Tracks a new purchase, saved by the next <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    void Add(CardPurchase purchase);
}
