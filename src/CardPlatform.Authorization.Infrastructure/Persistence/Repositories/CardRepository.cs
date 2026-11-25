using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Cards;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Authorization.Infrastructure.Persistence.Repositories;

internal sealed class CardRepository(AuthorizationDbContext dbContext) : ICardRepository
{
    public Task<Card?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Cards.SingleOrDefaultAsync(card => card.Id == id, cancellationToken);
    }

    public void Add(Card card)
    {
        dbContext.Cards.Add(card);
    }
}
