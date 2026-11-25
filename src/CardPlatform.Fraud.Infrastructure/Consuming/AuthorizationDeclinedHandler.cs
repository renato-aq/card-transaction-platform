using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Fraud.Application.Abstractions.Messaging;
using CardPlatform.Fraud.Application.Purchases;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Messaging.Consuming;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Fraud.Infrastructure.Consuming;

/// <summary>
/// Inbound adapter of <see cref="AuthorizationDeclined"/>: records a declined purchase, with its reason, to its card's
/// history through <see cref="RecordDeclinedPurchase"/>. Called by the consumer once per event id, inside its
/// transaction.
/// </summary>
internal sealed class AuthorizationDeclinedHandler(
    ICommandHandler<RecordDeclinedPurchase, RecordingOutcome> recordPurchase,
    ILogger<AuthorizationDeclinedHandler> logger) : IIntegrationEventHandler<AuthorizationDeclined>
{
    public async Task HandleAsync(
        AuthorizationDeclined integrationEvent,
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(envelope);

        Result<RecordingOutcome> result = await recordPurchase.HandleAsync(
            new RecordDeclinedPurchase(
                integrationEvent.AuthorizationId,
                integrationEvent.CardId,
                integrationEvent.MerchantAccountId,
                integrationEvent.Amount,
                integrationEvent.Currency,
                integrationEvent.CreatedAt,
                integrationEvent.Reason),
            cancellationToken).ConfigureAwait(false);

        AuthorizationEventRecording.Complete(result, envelope, logger);
    }
}
