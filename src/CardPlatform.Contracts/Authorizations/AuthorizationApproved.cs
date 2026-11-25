namespace CardPlatform.Contracts.Authorizations;

/// <summary>A purchase was approved and its amount is held in the Ledger.</summary>
public sealed record AuthorizationApproved(
    Guid AuthorizationId,
    Guid CardId,
    Guid MerchantAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt) : IAuthorizationEvent;
