using CardPlatform.Fraud.Domain.Common;

namespace CardPlatform.Fraud.Application.Abstractions.Messaging;

/// <summary>
/// Inbound port for a use case that changes state. Command handlers go through the domain model and the repositories
/// (ADR 0004). Expected failures are returned as a failed <see cref="Result{T}"/>; exceptions are reserved for
/// unexpected failures.
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
