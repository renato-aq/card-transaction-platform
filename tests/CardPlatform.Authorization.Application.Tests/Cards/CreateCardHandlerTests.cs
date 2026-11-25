using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Application.Tests.Cards;

public class CreateCardHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private readonly FakeLedgerClient _ledger = new();
    private readonly FakeCardRepository _cards = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Opens_the_ledger_account_and_saves_an_active_card_linked_to_it()
    {
        Guid ledgerAccountId = Guid.NewGuid();
        _ledger.OpenCardAccountResult = Result.Success(new LedgerAccount(ledgerAccountId, "BRL", 0m));

        Result<CardDto> result = await CreateHandler().HandleAsync(new CreateCard("BRL"));

        Assert.True(result.IsSuccess);
        Assert.Equal(["BRL"], _ledger.OpenedCurrencies);
        Card saved = Assert.Single(_cards.Added);
        Assert.Equal(ledgerAccountId, saved.LedgerAccountId);
        Assert.Equal(CardStatus.Active, saved.Status);
        Assert.Equal(Now, saved.CreatedAt);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(new CardDto(saved.Id, ledgerAccountId, "BRL", CardStatus.Active), result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("brl")]
    [InlineData("REAL")]
    public async Task Invalid_currency_fails_without_calling_the_ledger(string? currency)
    {
        Result<CardDto> result = await CreateHandler().HandleAsync(new CreateCard(currency));

        Assert.True(result.IsFailure);
        Assert.Equal(Currency.InvalidCurrencyCode, result.Error.Code);
        Assert.Empty(_ledger.OpenedCurrencies);
        Assert.Empty(_cards.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Ledger_failure_is_returned_and_no_card_is_saved()
    {
        _ledger.OpenCardAccountResult = Result.Failure<LedgerAccount>(LedgerErrors.Unavailable());

        Result<CardDto> result = await CreateHandler().HandleAsync(new CreateCard("BRL"));

        Assert.True(result.IsFailure);
        Assert.Equal(LedgerErrors.UnavailableCode, result.Error.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Empty(_cards.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private CreateCardHandler CreateHandler()
    {
        return new CreateCardHandler(_ledger, _cards, _unitOfWork, new FixedTimeProvider(Now));
    }
}
