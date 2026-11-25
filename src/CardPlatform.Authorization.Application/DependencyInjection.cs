using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Application.Abstractions.Messaging;
using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Cards;
using CardPlatform.Authorization.Application.Idempotency;

using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Authorization.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the command handlers of the Authorization. Query handlers read the database directly, so they are
    /// registered by the Infrastructure layer. Handlers need a <see cref="TimeProvider"/>, registered by the
    /// composition root.
    /// <para>
    /// Purchases are authorized only through <see cref="IdempotentAuthorizePurchase"/> (ADR 0008): its handler wraps an
    /// <see cref="AuthorizePurchaseHandler"/> whose save is deferred (<see cref="DeferredUnitOfWork"/>), so the
    /// authorization and the response of its idempotency key are saved together by the scoped
    /// <see cref="IUnitOfWork"/>. It also needs an <see cref="IAuthorizationResponseFactory"/>, registered by the API.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAuthorizationApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICommandHandler<CreateCard, CardDto>, CreateCardHandler>();
        services.AddScoped<ICommandHandler<BlockCard, CardDto>, BlockCardHandler>();
        services.AddScoped<ICommandHandler<CaptureAuthorization, AuthorizationDto>, CaptureAuthorizationHandler>();
        services.AddScoped<ICommandHandler<VoidAuthorization, AuthorizationDto>, VoidAuthorizationHandler>();
        services.AddScoped<ICommandHandler<IdempotentAuthorizePurchase, StoredResponse>>(static provider =>
            new IdempotentAuthorizePurchaseHandler(
                new AuthorizePurchaseHandler(
                    provider.GetRequiredService<ILedgerClient>(),
                    provider.GetRequiredService<ICardRepository>(),
                    provider.GetRequiredService<IAuthorizationRepository>(),
                    new DeferredUnitOfWork(),
                    provider.GetRequiredService<TimeProvider>()),
                provider.GetRequiredService<IIdempotencyKeyStore>(),
                provider.GetRequiredService<IUnitOfWork>(),
                provider.GetRequiredService<IAuthorizationResponseFactory>(),
                provider.GetRequiredService<TimeProvider>()));

        return services;
    }
}
