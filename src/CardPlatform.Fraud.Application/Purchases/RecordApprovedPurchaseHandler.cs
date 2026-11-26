using CardPlatform.Fraud.Application.Abstractions.Messaging;
using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Application.Signals;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Fraud.Domain.Rules;

namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>
/// Adds an approved purchase to its card's history and judges it by the <see cref="FraudRules"/> against the card's
/// activity before it; a suspicion is written to the outbox as a <c>FraudSuspected</c>, saved together with the purchase.
/// A purchase already recorded (the same event, or another one of the same authorization, arrived first) is left as it
/// is and not judged again, so an authorization raises at most one suspicion.
/// </summary>
public sealed class RecordApprovedPurchaseHandler(
    ICardPurchaseRepository purchases,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    FraudRules rules,
    TimeProvider timeProvider) : ICommandHandler<RecordApprovedPurchase, ApprovedPurchaseRecording>
{
    public async Task<Result<ApprovedPurchaseRecording>> HandleAsync(
        RecordApprovedPurchase command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await purchases.GetByIdAsync(command.AuthorizationId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return Result.Success(new ApprovedPurchaseRecording(RecordingOutcome.AlreadyRecorded));
        }

        Result<Money> amount = Money.Create(command.Amount, command.Currency);
        if (amount.IsFailure)
        {
            return Result.Failure<ApprovedPurchaseRecording>(amount.Error);
        }

        Result<CardPurchase> purchase = CardPurchase.Approve(
            command.AuthorizationId, command.CardId, command.MerchantAccountId, amount.Value, command.AuthorizedAt);
        if (purchase.IsFailure)
        {
            return Result.Failure<ApprovedPurchaseRecording>(purchase.Error);
        }

        CardActivity activity = await purchases
            .GetActivityAsync(purchase.Value, rules.Velocity.Window, cancellationToken)
            .ConfigureAwait(false);
        FraudSuspicion? suspicion = rules.Evaluate(purchase.Value, activity);

        purchases.Add(purchase.Value);
        if (suspicion is not null)
        {
            outbox.AddSuspicion(suspicion, timeProvider.GetUtcNow().TruncateToMicroseconds());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new ApprovedPurchaseRecording(RecordingOutcome.Recorded, suspicion));
    }
}
