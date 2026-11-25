using CardPlatform.Fraud.Application;
using CardPlatform.Fraud.Infrastructure;

namespace CardPlatform.Fraud.Worker;

/// <summary>
/// Composition root of the Fraud Worker, shared by <c>Program</c> and the integration tests so both start the same
/// host. It has no behaviour yet: consuming the authorization events and the fraud rules come in later steps.
/// </summary>
public static class FraudWorkerHost
{
    /// <summary>Creates the host builder with the service defaults and the Application and Infrastructure layers.</summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        builder.AddFraudInfrastructure();
        builder.Services.AddFraudApplication();

        return builder;
    }

    /// <summary>Prepares a built host before it starts: in Development, applies the EF Core migrations.</summary>
    public static async Task InitializeAsync(IHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            // Development only: other environments apply migrations as an explicit deployment step.
            await host.Services.ApplyFraudMigrationsAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
