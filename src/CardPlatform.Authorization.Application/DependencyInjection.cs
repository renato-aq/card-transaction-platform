using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Cards;

using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Authorization.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the command handlers of the Authorization. Query handlers read the database directly, so they are
    /// registered by the Infrastructure layer. Handlers need a <see cref="TimeProvider"/>, registered by the
    /// composition root.
    /// </summary>
    public static IServiceCollection AddAuthorizationApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICommandHandler<CreateCard, CardDto>, CreateCardHandler>();
        services.AddScoped<ICommandHandler<BlockCard, CardDto>, BlockCardHandler>();
        services.AddScoped<ICommandHandler<AuthorizePurchase, AuthorizationDto>, AuthorizePurchaseHandler>();

        return services;
    }
}
