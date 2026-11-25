using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Authorizations;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Authorization.Infrastructure.Persistence.Repositories;

internal sealed class AuthorizationRepository(AuthorizationDbContext dbContext) : IAuthorizationRepository
{
    public Task<CardAuthorization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.Authorizations.SingleOrDefaultAsync(authorization => authorization.Id == id, cancellationToken);
    }

    public void Add(CardAuthorization authorization)
    {
        dbContext.Authorizations.Add(authorization);
    }
}
