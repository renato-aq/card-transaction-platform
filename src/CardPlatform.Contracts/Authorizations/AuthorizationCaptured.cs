namespace CardPlatform.Contracts.Authorizations;

/// <summary>An approved authorization was captured: the held amount was charged.</summary>
/// <param name="CompletedAt">When it was captured.</param>
public sealed record AuthorizationCaptured(
    Guid AuthorizationId,
    Guid CardId,
    Guid MerchantAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset CompletedAt) : IAuthorizationEvent;
