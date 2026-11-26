using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>
/// What capturing and voiding share: completes the recorded purchase or, when its approval is not in the history (e.g.
/// it expired from the topic before this consumer first read it), records it from the completion event, which carries
/// the whole purchase.
/// </summary>
internal static class PurchaseCompletion
{
    public static async Task<Result<RecordingOutcome>> RecordAsync(
        ICardPurchaseRepository purchases,
        IUnitOfWork unitOfWork,
        PurchaseDetails details,
        Func<CardPurchase, Result> complete,
        CancellationToken cancellationToken)
    {
        CardPurchase? existing = await purchases.GetByIdAsync(details.AuthorizationId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            PurchaseStatus before = existing.Status;
            Result completed = complete(existing);
            if (completed.IsFailure)
            {
                return Result.Failure<RecordingOutcome>(completed.Error);
            }

            if (existing.Status == before)
            {
                return Result.Success(RecordingOutcome.AlreadyRecorded);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(RecordingOutcome.Updated);
        }

        Result<Money> amount = Money.Create(details.Amount, details.Currency);
        if (amount.IsFailure)
        {
            return Result.Failure<RecordingOutcome>(amount.Error);
        }

        Result<CardPurchase> purchase = CardPurchase.Approve(
            details.AuthorizationId, details.CardId, details.MerchantAccountId, amount.Value, details.AuthorizedAt);
        if (purchase.IsFailure)
        {
            return Result.Failure<RecordingOutcome>(purchase.Error);
        }

        // A new approved purchase can always be completed.
        Result result = complete(purchase.Value);
        if (result.IsFailure)
        {
            return Result.Failure<RecordingOutcome>(result.Error);
        }

        purchases.Add(purchase.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(RecordingOutcome.Recorded);
    }

    internal sealed record PurchaseDetails(
        Guid AuthorizationId,
        Guid CardId,
        Guid MerchantAccountId,
        decimal Amount,
        string Currency,
        DateTimeOffset AuthorizedAt);
}
