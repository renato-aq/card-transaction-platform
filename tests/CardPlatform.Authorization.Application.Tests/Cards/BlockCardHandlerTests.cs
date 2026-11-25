using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Application.Tests.Cards;

public class BlockCardHandlerTests
{
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Blocks_the_card_and_saves_it()
    {
        Card card = Card.Create(Guid.NewGuid(), Currency.Create("BRL").Value, DateTimeOffset.UtcNow);
        BlockCardHandler handler = new(new FakeCardRepository(card), _unitOfWork);

        Result<CardDto> result = await handler.HandleAsync(new BlockCard(card.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(CardStatus.Blocked, result.Value.Status);
        Assert.Equal(CardStatus.Blocked, card.Status);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Unknown_card_fails_with_card_not_found()
    {
        BlockCardHandler handler = new(new FakeCardRepository(), _unitOfWork);

        Result<CardDto> result = await handler.HandleAsync(new BlockCard(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal(CardErrors.CardNotFoundCode, result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }
}
