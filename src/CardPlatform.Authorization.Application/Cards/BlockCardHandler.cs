using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Cards;

public sealed class BlockCardHandler(ICardRepository cards, IUnitOfWork unitOfWork)
    : ICommandHandler<BlockCard, CardDto>
{
    public async Task<Result<CardDto>> HandleAsync(BlockCard command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Card? card = await cards.GetByIdAsync(command.CardId, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return Result.Failure<CardDto>(CardErrors.CardNotFound(command.CardId));
        }

        card.Block();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(CardDto.From(card));
    }
}
