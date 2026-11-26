using CardPlatform.Fraud.Application.Abstractions.Messaging;
using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>
/// Adds a declined purchase, with its reason, to its card's history. A purchase already recorded is left as it is.
/// </summary>
public sealed class RecordDeclinedPurchaseHandler(ICardPurchaseRepository purchases, IUnitOfWork unitOfWork)
    : ICommandHandler<RecordDeclinedPurchase, RecordingOutcome>
{
    public async Task<Result<RecordingOutcome>> HandleAsync(
        RecordDeclinedPurchase command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await purchases.GetByIdAsync(command.AuthorizationId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return Result.Success(RecordingOutcome.AlreadyRecorded);
        }

        Result<Money> amount = Money.Create(command.Amount, command.Currency);
        if (amount.IsFailure)
        {
            return Result.Failure<RecordingOutcome>(amount.Error);
        }

        Result<CardPurchase> purchase = CardPurchase.Decline(
            command.AuthorizationId,
            command.CardId,
            command.MerchantAccountId,
            amount.Value,
            command.Reason,
            command.AuthorizedAt);
        if (purchase.IsFailure)
        {
            return Result.Failure<RecordingOutcome>(purchase.Error);
        }

        purchases.Add(purchase.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(RecordingOutcome.Recorded);
    }
}
