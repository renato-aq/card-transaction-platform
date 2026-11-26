namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>Records that the Authorization declined a purchase (event <c>authorization.declined</c>).</summary>
/// <param name="Reason">The Authorization's snake_case decline code (e.g. <c>insufficient_funds</c>).</param>
public sealed record RecordDeclinedPurchase(
    Guid AuthorizationId,
    Guid CardId,
    Guid MerchantAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset AuthorizedAt,
    string Reason);
