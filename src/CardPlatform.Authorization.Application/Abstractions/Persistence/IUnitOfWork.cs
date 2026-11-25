namespace CardPlatform.Authorization.Application.Abstractions.Persistence;

/// <summary>
/// Outbound port that persists every change tracked by the repositories atomically.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Saves every tracked change in one database transaction.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
