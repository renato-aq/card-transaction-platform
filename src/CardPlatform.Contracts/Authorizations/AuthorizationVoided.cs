namespace CardPlatform.Contracts.Authorizations;

/// <summary>An approved authorization was voided: the hold was released without charging.</summary>
/// <param name="CompletedAt">When it was voided.</param>
public sealed record AuthorizationVoided(
    Guid AuthorizationId,
    Guid CardId,
    Guid MerchantAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset CompletedAt) : IAuthorizationEvent;
