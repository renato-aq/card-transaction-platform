using CardPlatform.Fraud.Application.Abstractions.Messaging;
using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Common;

namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>
/// Marks a recorded approved purchase as captured; records it as approved and captured when its approval is not in the
/// history. Capturing a purchase already captured changes nothing; a declined or voided one fails with
/// <see cref="Domain.Purchases.CardPurchase.NotCompletableCode"/>.
/// </summary>
public sealed class RecordCapturedPurchaseHandler(ICardPurchaseRepository purchases, IUnitOfWork unitOfWork)
    : ICommandHandler<RecordCapturedPurchase, RecordingOutcome>
{
    public Task<Result<RecordingOutcome>> HandleAsync(
        RecordCapturedPurchase command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return PurchaseCompletion.RecordAsync(
            purchases,
            unitOfWork,
            new PurchaseCompletion.PurchaseDetails(
                command.AuthorizationId,
                command.CardId,
                command.MerchantAccountId,
                command.Amount,
                command.Currency,
                command.AuthorizedAt),
            purchase => purchase.Capture(command.CompletedAt),
            cancellationToken);
    }
}
