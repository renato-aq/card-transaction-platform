namespace CardPlatform.Contracts.Authorizations;

/// <summary>A purchase was declined.</summary>
/// <param name="Reason">Why, as a snake_case code (see <see cref="DeclineReasons"/>).</param>
public sealed record AuthorizationDeclined(
    Guid AuthorizationId,
    Guid CardId,
    Guid MerchantAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    string Reason) : IAuthorizationEvent;
