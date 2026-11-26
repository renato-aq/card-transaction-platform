using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Fraud.Application.Abstractions.Messaging;
using CardPlatform.Fraud.Application.Purchases;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Messaging.Consuming;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Fraud.Infrastructure.Consuming;

/// <summary>
/// Inbound adapter of <see cref="AuthorizationCaptured"/>: records that a purchase was captured in its card's history
/// through <see cref="RecordCapturedPurchase"/>. Called by the consumer once per event id, inside its transaction.
/// </summary>
internal sealed class AuthorizationCapturedHandler(
    ICommandHandler<RecordCapturedPurchase, RecordingOutcome> recordPurchase,
    ILogger<AuthorizationCapturedHandler> logger) : IIntegrationEventHandler<AuthorizationCaptured>
{
    public async Task HandleAsync(
        AuthorizationCaptured integrationEvent,
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(envelope);

        Result<RecordingOutcome> result = await recordPurchase.HandleAsync(
            new RecordCapturedPurchase(
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
