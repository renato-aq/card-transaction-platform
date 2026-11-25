using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Application.Abstractions.Persistence;

/// <summary>Outbound port to the recorded purchases of the cards.</summary>
public interface ICardPurchaseRepository
{
    /// <summary>The purchase of the authorization <paramref name="authorizationId"/>, or <see langword="null"/>.</summary>
    Task<CardPurchase?> GetByIdAsync(Guid authorizationId, CancellationToken cancellationToken = default);

    /// <summary>Every purchase recorded for <paramref name="cardId"/> (empty when there is none).</summary>
    Task<CardHistory> GetHistoryAsync(Guid cardId, CancellationToken cancellationToken = default);

    /// <summary>Tracks a new purchase, saved by the next <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    void Add(CardPurchase purchase);
}
