using CardPlatform.Authorization.Domain.Cards;

namespace CardPlatform.Authorization.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port for loading and adding <see cref="Card"/> aggregates. Changes are persisted by
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface ICardRepository
{
    Task<Card?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds a new card, inserted by the next <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    void Add(Card card);
}
