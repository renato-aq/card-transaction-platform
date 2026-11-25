using CardPlatform.Ledger.Domain.Holds;

namespace CardPlatform.Ledger.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port for loading and adding <see cref="Hold"/> aggregates. Changes are persisted by
/// <see cref="IUnitOfWork.SaveChangesAsync"/>.
/// </summary>
public interface IHoldRepository
{
    Task<Hold?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Hold hold);
}
