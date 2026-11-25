namespace CardPlatform.Authorization.Application.Idempotency;

/// <summary>
/// The response recorded with an idempotency key and replayed, unchanged, for every repetition of the request: the
/// HTTP status code and the body, produced by the inbound adapter (see <see cref="IAuthorizationResponseFactory"/>)
/// and kept opaque by the Application. <see cref="AuthorizationId"/> is the authorization the response is about.
/// </summary>
public sealed record StoredResponse(Guid AuthorizationId, int StatusCode, string Body);
