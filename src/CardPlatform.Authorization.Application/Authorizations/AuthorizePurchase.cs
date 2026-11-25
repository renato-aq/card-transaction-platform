namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// Approves or declines a purchase of <paramref name="Amount"/> on a card, to be paid to the merchant's Ledger account
/// <paramref name="MerchantAccountId"/>. Every field arrives as raw input and is validated by the handler;
/// <paramref name="Currency"/> is an ISO 4217 code. A decline is a successful result (an authorization with status
/// <c>Declined</c>), not an error.
/// <para>
/// <paramref name="AuthorizationId"/> is the id to give the authorization (and its hold in the Ledger). The
/// idempotency layer reserves it per <c>Idempotency-Key</c>, so a retry of the same purchase reuses the same hold
/// (ADR 0008); when it is <see langword="null"/> the handler creates a new one.
/// </para>
/// </summary>
public sealed record AuthorizePurchase(
    Guid? CardId,
    Guid? MerchantAccountId,
    decimal? Amount,
    string? Currency,
    Guid? AuthorizationId = null);
