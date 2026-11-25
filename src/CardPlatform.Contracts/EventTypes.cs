using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

using CardPlatform.Contracts.Authorizations;
using CardPlatform.Contracts.Fraud;

namespace CardPlatform.Contracts;

/// <summary>
/// Registry of the integration events: maps the wire name carried in <see cref="EventEnvelope.Type"/> to the payload
/// type and back. Names are part of the contract and never change; a compatible change bumps the version, an
/// incompatible one becomes a new event type.
/// </summary>
public static class EventTypes
{
    public const string AuthorizationApproved = "authorization.approved";

    public const string AuthorizationDeclined = "authorization.declined";

    public const string AuthorizationCaptured = "authorization.captured";

    public const string AuthorizationVoided = "authorization.voided";

    public const string FraudSuspected = "fraud.suspected";

    private static readonly EventType[] Registered =
    [
        new(AuthorizationApproved, 1, typeof(AuthorizationApproved)),
        new(AuthorizationDeclined, 1, typeof(AuthorizationDeclined)),
        new(AuthorizationCaptured, 1, typeof(AuthorizationCaptured)),
        new(AuthorizationVoided, 1, typeof(AuthorizationVoided)),
        new(FraudSuspected, 1, typeof(FraudSuspected)),
    ];

    private static readonly FrozenDictionary<string, EventType> ByName =
        Registered.ToFrozenDictionary(eventType => eventType.Name, StringComparer.Ordinal);

    private static readonly FrozenDictionary<Type, EventType> ByClrType =
        Registered.ToFrozenDictionary(eventType => eventType.ClrType);

    /// <summary>Every registered event type.</summary>
    public static IReadOnlyCollection<EventType> All => Registered;

    /// <summary>Finds the event type registered under <paramref name="name"/>.</summary>
    public static bool TryFind(string name, [NotNullWhen(true)] out EventType? eventType)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ByName.TryGetValue(name, out eventType);
    }

    /// <summary>Gets the event type of the payload type <paramref name="clrType"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="clrType"/> is not a registered event.</exception>
    public static EventType Of(Type clrType)
    {
        ArgumentNullException.ThrowIfNull(clrType);

        return ByClrType.TryGetValue(clrType, out EventType? eventType)
            ? eventType
            : throw new ArgumentException($"{clrType.FullName} is not a registered integration event.", nameof(clrType));
    }
}
