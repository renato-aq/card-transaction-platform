using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Authorization.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the command handlers of the Authorization. Query handlers read the database directly, so they are
    /// registered by the Infrastructure layer. There are no use cases yet.
    /// </summary>
    public static IServiceCollection AddAuthorizationApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
