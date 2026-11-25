using CardPlatform.Authorization.Application.Authorizations;

namespace CardPlatform.Authorization.Application.Idempotency;

/// <summary>
/// Port implemented by the inbound adapter (the API): turns a recorded authorization into the response stored with its
/// idempotency key. The status code and body are protocol details, so the Application stores them without
/// interpreting them, and the first answer and every replay come from the same stored text.
/// </summary>
public interface IAuthorizationResponseFactory
{
    StoredResponse Create(AuthorizationDto authorization);
}
