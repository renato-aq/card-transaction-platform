using CardPlatform.Authorization.Application.Abstractions.Ledger;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CardPlatform.Authorization.Infrastructure.Tests.Ledger;

/// <summary>
/// Builds the Ledger client through the same registrations as the Authorization API (<c>AddServiceDefaults</c>, which
/// adds service discovery and the standard resilience handler to every client, plus <c>AddLedgerClient</c>), with
/// only the primary handler replaced by a <see cref="FakeLedgerHandler"/>. Timeouts are shortened to keep tests fast.
/// </summary>
internal sealed class LedgerClientHarness : IDisposable
{
    public const string LedgerAddress = "http://ledger.test";

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMilliseconds(300);

    public static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(1);

    public const int MaxRetryAttempts = 2;

    private readonly IHost _host;

    public LedgerClientHarness()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
            DisableDefaults = true,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // What Aspire injects for .WithReference(ledgerApi); resolved from https+http://ledger-api.
            ["services:ledger-api:http:0"] = LedgerAddress,
            ["Ledger:AttemptTimeout"] = AttemptTimeout.ToString(),
            ["Ledger:TotalRequestTimeout"] = TotalRequestTimeout.ToString(),
            ["Ledger:MaxRetryAttempts"] = MaxRetryAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Ledger:RetryBaseDelay"] = TimeSpan.FromMilliseconds(10).ToString(),
        });

        builder.AddServiceDefaults();
        builder.Services.AddLedgerClient();
        builder.Services.AddHttpClient(DependencyInjection.LedgerHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Handler);

        _host = builder.Build();
        Client = _host.Services.GetRequiredService<ILedgerClient>();
    }

    public FakeLedgerHandler Handler { get; } = new();

    public ILedgerClient Client { get; }

    public IServiceProvider Services => _host.Services;

    public void Dispose()
    {
        _host.Dispose();
        Handler.Dispose();
    }
}
