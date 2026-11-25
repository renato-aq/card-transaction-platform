namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// Captures an approved authorization: its hold is captured in the Ledger to the authorization's merchant account and
/// the authorization becomes <c>Captured</c>. See <see cref="CaptureAuthorizationHandler"/> for the failures.
/// </summary>
public sealed record CaptureAuthorization(Guid AuthorizationId);
