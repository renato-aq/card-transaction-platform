using CardPlatform.Fraud.Application.Abstractions.Messaging;
using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>
/// Adds an approved purchase to its card's history. A purchase already recorded (the same event, or another one of the
/// same authorization, arrived first) is left as it is.
/// </summary>
public sealed class RecordApprovedPurchaseHandler(ICardPurchaseRepository purchases, IUnitOfWork unitOfWork)
    : ICommandHandler<RecordApprovedPurchase, RecordingOutcome>
{
    public async Task<Result<RecordingOutcome>> HandleAsync(
        RecordApprovedPurchase command,
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

        Result<CardPurchase> purchase = CardPurchase.Approve(
            command.AuthorizationId, command.CardId, command.MerchantAccountId, amount.Value, command.AuthorizedAt);
        if (purchase.IsFailure)
        {
            return Result.Failure<RecordingOutcome>(purchase.Error);
        }

        purchases.Add(purchase.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(RecordingOutcome.Recorded);
    }
}
