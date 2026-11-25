using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Accounts;
using CardPlatform.Ledger.Application.Concurrency;
using CardPlatform.Ledger.Application.Holds;
using CardPlatform.Ledger.Application.Transactions;

using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Ledger.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the command handlers. Query handlers read the database directly, so they are registered by
    /// the Infrastructure layer. Handlers need a <see cref="TimeProvider"/>, registered by the composition root.
    /// <see cref="PostTransaction"/> and the hold commands (<see cref="PlaceHold"/>, <see cref="CaptureHold"/>,
    /// <see cref="VoidHold"/>) are decorated with <see cref="RetryOnConcurrencyConflictDecorator{TCommand, TResult}"/>
    /// (ADR 0006); the decorator and the handler share the scoped <see cref="IUnitOfWork"/>.
    /// </summary>
    public static IServiceCollection AddLedgerApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICommandHandler<OpenAccount, AccountDto>, OpenAccountHandler>();

        services.AddSingleton<IRetryDelay, JitteredRetryDelay>();
        services.AddRetryingCommandHandler<PostTransaction, LedgerTransactionDto, PostTransactionHandler>();
        services.AddRetryingCommandHandler<PlaceHold, PlaceHoldResult, PlaceHoldHandler>();
        services.AddRetryingCommandHandler<CaptureHold, HoldDto, CaptureHoldHandler>();
        services.AddRetryingCommandHandler<VoidHold, HoldDto, VoidHoldHandler>();

        return services;
    }

    // Registers THandler as the handler of TCommand, wrapped in the concurrency retry decorator.
    private static void AddRetryingCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services)
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResult>>(static provider =>
            new RetryOnConcurrencyConflictDecorator<TCommand, TResult>(
                provider.GetRequiredService<THandler>(),
                provider.GetRequiredService<IUnitOfWork>(),
                provider.GetRequiredService<IRetryDelay>()));
    }
}
