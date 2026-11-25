using CardPlatform.Authorization.Application.Authorizations;

namespace CardPlatform.Authorization.Application.Idempotency;

/// <summary>
/// Authorizes <paramref name="Purchase"/> at most once per <paramref name="IdempotencyKey"/> (the
/// <c>Idempotency-Key</c> header of <c>POST /authorizations</c>), see ADR 0008. The key arrives as raw input and is
/// validated by the handler.
/// </summary>
public sealed record IdempotentAuthorizePurchase(string? IdempotencyKey, AuthorizePurchase Purchase);
