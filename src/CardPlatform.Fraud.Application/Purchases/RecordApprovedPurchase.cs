namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>Records that the Authorization approved a purchase (event <c>authorization.approved</c>).</summary>
public sealed record RecordApprovedPurchase(
    Guid AuthorizationId,
    Guid CardId,
    Guid MerchantAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset AuthorizedAt);
