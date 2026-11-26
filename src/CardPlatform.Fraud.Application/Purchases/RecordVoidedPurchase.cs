namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>Records that an approved purchase was voided (event <c>authorization.voided</c>).</summary>
public sealed record RecordVoidedPurchase(
    Guid AuthorizationId,
    Guid CardId,
    Guid MerchantAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset AuthorizedAt,
    DateTimeOffset CompletedAt);
