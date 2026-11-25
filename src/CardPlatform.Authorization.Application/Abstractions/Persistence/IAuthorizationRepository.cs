using CardPlatform.Authorization.Domain.Authorizations;

namespace CardPlatform.Authorization.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port for loading and adding <see cref="CardAuthorization"/> aggregates. Changes are persisted by
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IAuthorizationRepository
{
    /// <summary>
    /// Loads an authorization to change it (tracked: its changes are saved by the next
    /// <see cref="IUnitOfWork.SaveChangesAsync"/>), or <see langword="null"/> when it does not exist.
    /// </summary>
    Task<CardAuthorization?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds a new authorization, inserted by the next <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    void Add(CardAuthorization authorization);
}
