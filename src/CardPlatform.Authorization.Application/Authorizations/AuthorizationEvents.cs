using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Application.Common;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;

namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// Maps a <see cref="CardAuthorization"/> to the integration event of its current status (approved, declined, captured
/// or voided) and records it in the outbox. Every write of a decision or of a completed status records the event in the
/// same unit of work, so the event exists if and only if the status was saved.
/// </summary>
/// <remarks>
/// The event id is deterministic: a name-based UUID (<see cref="NameBasedGuid"/>) of the authorization id and the event
/// type. Each status is reached once per authorization, but ADR 0007 lets concurrent captures or voids of the same
/// authorization both save the same final status (no concurrency token on <c>authorizations</c>); both then write an
/// outbox row, and the rows carry the same event id, so consumers, which process each event id once (ADR 0003), drop
/// the second. Only <see cref="AuthorizationCaptured.CompletedAt"/> / <see cref="AuthorizationVoided.CompletedAt"/> may
/// differ between such duplicates, as it does for the authorization itself (last writer wins).
/// </remarks>
public static class AuthorizationEvents
{
    /// <summary>Namespace of the event ids of authorization events. Never changes: the ids are derived from it.</summary>
    public static readonly Guid EventIdNamespace = new("5f0b6a4e-2d1c-4c8e-9a43-7e2f1b9d6c31");

    /// <summary>
    /// Adds the event of <paramref name="authorization"/>'s current status to <paramref name="outbox"/>, saved by the
    /// next <see cref="IUnitOfWork.SaveChangesAsync"/> with the authorization.
    /// </summary>
    public static void AddEventOf(this IOutbox outbox, CardAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        IAuthorizationEvent @event = From(authorization);
        outbox.Add(@event, EventIdOf(authorization.Id, @event.GetType()), OccurredAt(authorization));
    }

    /// <summary>The event that describes <paramref name="authorization"/>'s current status.</summary>
    public static IAuthorizationEvent From(CardAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        Guid id = authorization.Id;
        Guid cardId = authorization.CardId;
        Guid merchant = authorization.MerchantAccountId;
        decimal amount = authorization.Amount.Amount;
        string currency = authorization.Amount.Currency.Code;
        DateTimeOffset createdAt = authorization.CreatedAt;

        return authorization.Status switch
        {
            AuthorizationStatus.Approved => new AuthorizationApproved(id, cardId, merchant, amount, currency, createdAt),
            AuthorizationStatus.Declined => new AuthorizationDeclined(
                id,
                cardId,
                merchant,
                amount,
                currency,
                createdAt,
                DeclineReasons.ToCode(authorization.DeclineReason)
                    ?? throw new InvalidOperationException($"Declined authorization {id} has no decline reason.")),
            AuthorizationStatus.Captured => new AuthorizationCaptured(
                id, cardId, merchant, amount, currency, createdAt, CompletedAt(authorization)),
            AuthorizationStatus.Voided => new AuthorizationVoided(
                id, cardId, merchant, amount, currency, createdAt, CompletedAt(authorization)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(authorization), authorization.Status, "Unknown authorization status."),
        };
    }

    /// <summary>
    /// The event id of the event of type <paramref name="eventType"/> (a registered <see cref="IAuthorizationEvent"/>
    /// type) about authorization <paramref name="authorizationId"/>: always the same for the same pair.
    /// </summary>
    public static Guid EventIdOf(Guid authorizationId, Type eventType)
    {
        string name = EventTypes.Of(eventType).Name;
        return NameBasedGuid.Create(EventIdNamespace, $"{authorizationId:D}/{name}");
    }

    /// <summary>When the event happened: the decision time, or the completion time once captured or voided.</summary>
    private static DateTimeOffset OccurredAt(CardAuthorization authorization)
    {
        return authorization.Status is AuthorizationStatus.Captured or AuthorizationStatus.Voided
            ? CompletedAt(authorization)
            : authorization.CreatedAt;
    }

    private static DateTimeOffset CompletedAt(CardAuthorization authorization)
    {
        return authorization.CompletedAt
            ?? throw new InvalidOperationException($"Authorization {authorization.Id} is {authorization.Status} but has no completion time.");
    }
}
