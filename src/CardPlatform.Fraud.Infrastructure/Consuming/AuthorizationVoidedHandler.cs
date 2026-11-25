using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Fraud.Application.Abstractions.Messaging;
using CardPlatform.Fraud.Application.Purchases;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Messaging.Consuming;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Fraud.Infrastructure.Consuming;

/// <summary>
/// Inbound adapter of <see cref="AuthorizationVoided"/>: records that a purchase was voided in its card's history
/// through <see cref="RecordVoidedPurchase"/>. Called by the consumer once per event id, inside its transaction.
/// </summary>
internal sealed class AuthorizationVoidedHandler(
    ICommandHandler<RecordVoidedPurchase, RecordingOutcome> recordPurchase,
    ILogger<AuthorizationVoidedHandler> logger) : IIntegrationEventHandler<AuthorizationVoided>
{
    public async Task HandleAsync(
        AuthorizationVoided integrationEvent,
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(envelope);

        Result<RecordingOutcome> result = await recordPurchase.HandleAsync(
            new RecordVoidedPurchase(
                integrationEvent.AuthorizationId,
                integrationEvent.CardId,
                integrationEvent.MerchantAccountId,
                integrationEvent.Amount,
                integrationEvent.Currency,
                integrationEvent.CreatedAt,
                integrationEvent.CompletedAt),
            cancellationToken).ConfigureAwait(false);

        AuthorizationEventRecording.Complete(result, envelope, logger);
    }
}
