using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Domain.Authorizations;

namespace CardPlatform.Authorization.Api.Endpoints;

/// <summary>
/// Body of <c>POST /authorizations</c>: a purchase of <see cref="Amount"/> in <see cref="Currency"/> (ISO 4217) on a
/// card, to be paid to the merchant's Ledger account <see cref="MerchantAccountId"/>.
/// </summary>
public sealed record AuthorizePurchaseRequest(Guid? CardId, Guid? MerchantAccountId, decimal? Amount, string? Currency);

/// <summary>
/// An authorization; <see cref="Status"/> is serialized in camelCase (<c>approved</c>, <c>declined</c>, ...) and
/// <see cref="DeclineReason"/> is a <c>snake_case</c> code (<c>card_blocked</c>, <c>currency_mismatch</c>,
/// <c>insufficient_funds</c>), <see langword="null"/> unless the purchase was declined. <see cref="CompletedAt"/> is when
/// the authorization was captured or voided, <see langword="null"/> before that.
/// </summary>
public sealed record AuthorizationResponse(
    Guid Id,
    AuthorizationStatus Status,
    string? DeclineReason,
    Guid CardId,
    Guid MerchantAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt)
{
    internal static AuthorizationResponse From(AuthorizationDto authorization)
    {
        return new AuthorizationResponse(
            authorization.Id,
            authorization.Status,
            authorization.DeclineReason,
            authorization.CardId,
            authorization.MerchantAccountId,
            authorization.Amount,
            authorization.Currency,
            authorization.CreatedAt,
            authorization.CompletedAt);
    }
}
