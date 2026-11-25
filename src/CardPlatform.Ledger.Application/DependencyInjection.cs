using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Accounts;
using CardPlatform.Ledger.Application.Concurrency;
using CardPlatform.Ledger.Application.Transactions;

using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Ledger.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the command handlers. Query handlers read the database directly, so they are registered by
    /// the Infrastructure layer. Handlers need a <see cref="TimeProvider"/>, registered by the composition root.
    /// <see cref="PostTransaction"/> is decorated with <see cref="RetryOnConcurrencyConflictDecorator{TCommand, TResult}"/>
    /// (ADR 0006); the decorator and the handler share the scoped <see cref="IUnitOfWork"/>.
    /// </summary>
    public static IServiceCollection AddLedgerApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICommandHandler<OpenAccount, AccountDto>, OpenAccountHandler>();

        services.AddSingleton<IRetryDelay, JitteredRetryDelay>();
        services.AddScoped<PostTransactionHandler>();
        services.AddScoped<ICommandHandler<PostTransaction, LedgerTransactionDto>>(static provider =>
            new RetryOnConcurrencyConflictDecorator<PostTransaction, LedgerTransactionDto>(
                provider.GetRequiredService<PostTransactionHandler>(),
                provider.GetRequiredService<IUnitOfWork>(),
                provider.GetRequiredService<IRetryDelay>()));

        return services;
    }
}
