using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Accounts;

using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Ledger.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the command handlers. Query handlers read the database directly, so they are registered by
    /// the Infrastructure layer. Handlers need a <see cref="TimeProvider"/>, registered by the composition root.
    /// </summary>
    public static IServiceCollection AddLedgerApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICommandHandler<OpenAccount, AccountDto>, OpenAccountHandler>();

        return services;
    }
}
