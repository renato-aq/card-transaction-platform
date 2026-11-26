using CardPlatform.Contracts;
using CardPlatform.Contracts.Fraud;
using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Rules;

namespace CardPlatform.Fraud.Application.Signals;

/// <summary>
/// Maps a <see cref="FraudSuspicion"/> to the integration event <see cref="FraudSuspected"/> and records it in the
/// outbox, saved in the same transaction as the purchase that raised it (and, when it comes from a consumed event, as
/// that event's processed mark).
/// </summary>
/// <remarks>
/// The event id is deterministic: a name-based UUID (<see cref="NameBasedGuid"/>) of the authorization id and the event
/// type. A purchase is judged once, when its approval is first recorded, so it raises at most one suspicion; should the
/// same approval ever be judged again (e.g. a database rebuilt and replayed from Kafka), the event carries the same id
/// and the consumers, which process each event id once (ADR 0011), drop the copy.
/// </remarks>
public static class FraudSignals
{
    /// <summary>Namespace of the event ids of fraud suspicions. Never changes: the ids are derived from it.</summary>
    public static readonly Guid EventIdNamespace = new("66fe9def-6b83-4446-b897-6b90c7c25617");

    /// <summary>
    /// Adds the <see cref="FraudSuspected"/> of <paramref name="suspicion"/>, detected at <paramref name="detectedAt"/>,
    /// to <paramref name="outbox"/>, saved by the next save with the purchase.
    /// </summary>
    public static void AddSuspicion(this IOutbox outbox, FraudSuspicion suspicion, DateTimeOffset detectedAt)
    {
        ArgumentNullException.ThrowIfNull(outbox);

        FraudSuspected @event = From(suspicion, detectedAt);
        outbox.Add(@event, EventIdOf(suspicion.AuthorizationId), @event.DetectedAt);
    }

    /// <summary>The event that announces <paramref name="suspicion"/>.</summary>
    public static FraudSuspected From(FraudSuspicion suspicion, DateTimeOffset detectedAt)
    {
        ArgumentNullException.ThrowIfNull(suspicion);

        return new FraudSuspected(suspicion.CardId, suspicion.AuthorizationId, suspicion.Rule, suspicion.Reason, detectedAt);
    }

    /// <summary>
    /// The event id of the <see cref="FraudSuspected"/> raised by authorization <paramref name="authorizationId"/>:
    /// always the same for the same authorization.
    /// </summary>
    public static Guid EventIdOf(Guid authorizationId)
    {
        return NameBasedGuid.Create(EventIdNamespace, $"{authorizationId:D}/{EventTypes.FraudSuspected}");
    }
}
