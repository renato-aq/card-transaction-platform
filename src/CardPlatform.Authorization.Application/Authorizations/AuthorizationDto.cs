using CardPlatform.Authorization.Domain.Authorizations;

namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// An authorization; <see cref="DeclineReason"/> is the <c>snake_case</c> code of the reason (see
/// <see cref="DeclineReasons"/>), <see langword="null"/> unless the purchase was declined.
/// </summary>
public sealed record AuthorizationDto(
    Guid Id,
    AuthorizationStatus Status,
    string? DeclineReason,
    Guid CardId,
    Guid MerchantAccountId,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt)
{
    internal static AuthorizationDto From(CardAuthorization authorization)
    {
        return new AuthorizationDto(
            authorization.Id,
            authorization.Status,
            DeclineReasons.ToCode(authorization.DeclineReason),
            authorization.CardId,
            authorization.MerchantAccountId,
            authorization.Amount.Amount,
            authorization.Amount.Currency.Code,
            authorization.CreatedAt);
    }
}
