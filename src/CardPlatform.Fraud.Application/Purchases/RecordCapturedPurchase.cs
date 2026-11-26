namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>Records that an approved purchase was captured (event <c>authorization.captured</c>).</summary>
public sealed record RecordCapturedPurchase(
    Guid AuthorizationId,
    Guid CardId,
    Guid MerchantAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset AuthorizedAt,
    DateTimeOffset CompletedAt);
