using System.Text.Json;

using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Idempotency;

using Microsoft.Extensions.Options;

using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace CardPlatform.Authorization.Api.Endpoints;

/// <summary>
/// Builds the response of <c>POST /authorizations</c> stored with the idempotency key (ADR 0008): <c>201 Created</c> and
/// the <see cref="AuthorizationResponse"/> serialized with the API's JSON options. Approved and declined authorizations
/// are both 201; the <c>Location</c> header is rebuilt from <see cref="StoredResponse.AuthorizationId"/>.
/// </summary>
internal sealed class AuthorizationResponseFactory(IOptions<JsonOptions> jsonOptions) : IAuthorizationResponseFactory
{
    public StoredResponse Create(AuthorizationDto authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return new StoredResponse(
            authorization.Id,
            StatusCodes.Status201Created,
            JsonSerializer.Serialize(AuthorizationResponse.From(authorization), jsonOptions.Value.SerializerOptions));
    }
}
