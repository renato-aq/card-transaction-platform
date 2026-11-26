using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Contracts;
using CardPlatform.Contracts.Fraud;
using CardPlatform.Messaging.Consuming;

using Microsoft.Extensions.Logging;

namespace CardPlatform.Authorization.Infrastructure.Consuming;

/// <summary>
/// Inbound adapter of <see cref="FraudSuspected"/>: blocks the suspected card through the existing
/// <see cref="BlockCard"/> command, which is idempotent (blocking a blocked card succeeds), so the next purchases of the
/// card are declined with <c>card_blocked</c>. Called by the consumer once per event id, inside its transaction: the
/// block and the processed mark are committed together (ADR 0011).
/// </summary>
/// <remarks>
/// The suspicion (card, authorization, rule and reason) is only logged; the card's contract does not change. A
/// suspicion of a card the Authorization does not know is logged and confirmed: there is nothing to block, and
/// retrying it would never succeed.
/// </remarks>
internal sealed partial class FraudSuspectedHandler(
    ICommandHandler<BlockCard, CardDto> blockCard,
    ILogger<FraudSuspectedHandler> logger) : IIntegrationEventHandler<FraudSuspected>
{
    public async Task HandleAsync(
        FraudSuspected integrationEvent,
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(envelope);

        Result<CardDto> result = await blockCard
            .HandleAsync(new BlockCard(integrationEvent.CardId), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            CardBlocked(
                logger,
                integrationEvent.CardId,
                integrationEvent.AuthorizationId,
                integrationEvent.Rule,
                integrationEvent.Reason,
                envelope.EventId);
            return;
        }

        if (result.Error.Code == CardErrors.CardNotFoundCode)
        {
            UnknownCard(
                logger,
                integrationEvent.CardId,
                integrationEvent.AuthorizationId,
                integrationEvent.Rule,
                integrationEvent.Reason,
                envelope.EventId);
            return;
        }

        // Not expected today (blocking only fails for an unknown card); never confirm an event that had no effect.
        throw new IntegrationEventRejectedException(
            $"Event {envelope.EventId} ({envelope.Type}) was rejected: {result.Error.Code}: {result.Error.Message}");
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Card {CardId} blocked: fraud suspected for authorization {AuthorizationId} ({Rule}): {Reason} (event {EventId}).")]
    private static partial void CardBlocked(
        ILogger logger,
        Guid cardId,
        Guid authorizationId,
        string rule,
        string reason,
        Guid eventId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Ignoring a fraud suspicion of unknown card {CardId} for authorization {AuthorizationId} ({Rule}): {Reason} (event {EventId}).")]
    private static partial void UnknownCard(
        ILogger logger,
        Guid cardId,
        Guid authorizationId,
        string rule,
        string reason,
        Guid eventId);
}
