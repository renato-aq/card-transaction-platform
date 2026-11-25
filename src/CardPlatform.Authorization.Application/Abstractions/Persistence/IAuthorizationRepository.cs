using CardPlatform.Authorization.Domain.Authorizations;

namespace CardPlatform.Authorization.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port for adding <see cref="CardAuthorization"/> aggregates. Changes are persisted by
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IAuthorizationRepository
{
    /// <summary>Adds a new authorization, inserted by the next <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    void Add(CardAuthorization authorization);
}
