using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Fraud.Application.Abstractions.Messaging;
using CardPlatform.Fraud.Application.Purchases;
using CardPlatform.Fraud.Domain.Common;
using CardPlatform.Messaging.Consuming;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Fraud.Infrastructure.Consuming;

/// <summary>
/// Inbound adapter of <see cref="AuthorizationApproved"/>: records an approved purchase to its card's history through
/// <see cref="RecordApprovedPurchase"/>. Called by the consumer once per event id, inside its transaction.
/// </summary>
internal sealed class AuthorizationApprovedHandler(
    ICommandHandler<RecordApprovedPurchase, RecordingOutcome> recordPurchase,
    ILogger<AuthorizationApprovedHandler> logger) : IIntegrationEventHandler<AuthorizationApproved>
{
    public async Task HandleAsync(
        AuthorizationApproved integrationEvent,
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(envelope);

        Result<RecordingOutcome> result = await recordPurchase.HandleAsync(
            new RecordApprovedPurchase(
                integrationEvent.AuthorizationId,
                integrationEvent.CardId,
                integrationEvent.MerchantAccountId,
                integrationEvent.Amount,
                integrationEvent.Currency,
                integrationEvent.CreatedAt),
            cancellationToken).ConfigureAwait(false);

        AuthorizationEventRecording.Complete(result, envelope, logger);
    }
}
