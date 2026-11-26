using CardPlatform.Fraud.Application.Abstractions.Messaging;
using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Common;

namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>
/// Marks a recorded approved purchase as voided; records it as approved and voided when its approval is not in the
/// history. Voiding a purchase already voided changes nothing; a declined or captured one fails with
/// <see cref="Domain.Purchases.CardPurchase.NotCompletableCode"/>.
/// </summary>
public sealed class RecordVoidedPurchaseHandler(ICardPurchaseRepository purchases, IUnitOfWork unitOfWork)
    : ICommandHandler<RecordVoidedPurchase, RecordingOutcome>
{
    public Task<Result<RecordingOutcome>> HandleAsync(
        RecordVoidedPurchase command,
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
            purchase => purchase.Void(command.CompletedAt),
            cancellationToken);
    }
}
