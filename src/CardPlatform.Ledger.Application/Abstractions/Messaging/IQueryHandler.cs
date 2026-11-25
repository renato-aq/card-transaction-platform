using CardPlatform.Ledger.Domain.Common;

namespace CardPlatform.Ledger.Application.Abstractions.Messaging;

/// <summary>
/// Inbound port for a use case that only reads state. Query handlers bypass the domain model and read
/// projections straight from the database, so their implementations live in Infrastructure (ADR 0004).
/// </summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
