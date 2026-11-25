using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Authorization.Infrastructure.Queries;

/// <summary>
/// Reads an authorization with a no-tracking projection, without loading the <c>CardAuthorization</c> aggregate
/// (pragmatic CQRS, ADR 0004).
/// </summary>
internal sealed class GetAuthorizationHandler(AuthorizationDbContext dbContext)
    : IQueryHandler<GetAuthorization, AuthorizationDto>
{
    public async Task<Result<AuthorizationDto>> HandleAsync(
        GetAuthorization query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var row = await dbContext.Authorizations
            .AsNoTracking()
            .Where(authorization => authorization.Id == query.AuthorizationId)
            .Select(static authorization => new
            {
                authorization.Id,
                authorization.Status,
                authorization.DeclineReason,
                authorization.CardId,
                authorization.MerchantAccountId,
                authorization.Amount.Amount,
                authorization.Amount.Currency,
                authorization.CreatedAt,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return Result.Failure<AuthorizationDto>(AuthorizationErrors.AuthorizationNotFound(query.AuthorizationId));
        }

        return Result.Success(new AuthorizationDto(
            row.Id,
            row.Status,
            DeclineReasons.ToCode(row.DeclineReason),
            row.CardId,
            row.MerchantAccountId,
            row.Amount,
            row.Currency.Code,
            row.CreatedAt));
    }
}
