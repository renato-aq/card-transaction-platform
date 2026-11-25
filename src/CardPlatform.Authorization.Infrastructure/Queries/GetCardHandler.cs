using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Authorization.Infrastructure.Queries;

/// <summary>
/// Reads a card with a no-tracking projection, without loading the <c>Card</c> aggregate (pragmatic CQRS, ADR 0004).
/// </summary>
internal sealed class GetCardHandler(AuthorizationDbContext dbContext) : IQueryHandler<GetCard, CardDto>
{
    public async Task<Result<CardDto>> HandleAsync(GetCard query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var row = await dbContext.Cards
            .AsNoTracking()
            .Where(card => card.Id == query.CardId)
            .Select(static card => new { card.Id, card.LedgerAccountId, card.Currency, card.Status })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return Result.Failure<CardDto>(CardErrors.CardNotFound(query.CardId));
        }

        return Result.Success(new CardDto(row.Id, row.LedgerAccountId, row.Currency.Code, row.Status));
    }
}
