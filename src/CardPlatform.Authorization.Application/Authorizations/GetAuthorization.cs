namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// Reads an authorization. Fails with <see cref="AuthorizationErrors.AuthorizationNotFoundCode"/> when it does not
/// exist. The handler lives in Infrastructure because it reads the database directly.
/// </summary>
public sealed record GetAuthorization(Guid AuthorizationId);
