using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Authorizations;

namespace CardPlatform.Authorization.Infrastructure.Persistence.Repositories;

internal sealed class AuthorizationRepository(AuthorizationDbContext dbContext) : IAuthorizationRepository
{
    public void Add(CardAuthorization authorization)
    {
        dbContext.Authorizations.Add(authorization);
    }
}
