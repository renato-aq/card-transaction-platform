using CardPlatform.Ledger.Domain.Holds;

namespace CardPlatform.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port for loading and adding <see cref="Hold"/> aggregates. Changes are persisted by
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IHoldRepository
{
    Task<Hold?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new hold, inserted by the next <see cref="IUnitOfWork.SaveChangesAsync"/>. When another request inserted
    /// a hold with the same id first, that save throws <see cref="ConcurrencyConflictException"/>, and a retry finds it.
    /// </summary>
    void Add(Hold hold);
}
