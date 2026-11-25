using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Fraud.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use cases of the Fraud Worker. There are none yet: consuming the authorization events and the fraud
    /// rules come in later steps.
    /// </summary>
    public static IServiceCollection AddFraudApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
