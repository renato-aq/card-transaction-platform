using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Application.Cards;

/// <summary>
/// Validates the currency, opens the card account in the Ledger and only then saves the card, so a Ledger failure
/// (e.g. <see cref="LedgerErrors.UnavailableCode"/>) is returned as it is and no card is saved. If saving the card
/// fails after the Ledger account was opened, that account stays in the Ledger with a zero balance and no card.
/// </summary>
public sealed class CreateCardHandler(
    ILedgerClient ledger,
    ICardRepository cards,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<CreateCard, CardDto>
{
    public async Task<Result<CardDto>> HandleAsync(CreateCard command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<Currency> currency = Currency.Create(command.Currency);
        if (currency.IsFailure)
        {
            return Result.Failure<CardDto>(currency.Error);
        }

        Result<LedgerAccount> account = await ledger
            .OpenCardAccountAsync(currency.Value.Code, cancellationToken)
            .ConfigureAwait(false);
        if (account.IsFailure)
        {
            return Result.Failure<CardDto>(account.Error);
        }

        Card card = Card.Create(account.Value.Id, currency.Value, timeProvider.GetUtcNow());
        cards.Add(card);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(CardDto.From(card));
    }
}
