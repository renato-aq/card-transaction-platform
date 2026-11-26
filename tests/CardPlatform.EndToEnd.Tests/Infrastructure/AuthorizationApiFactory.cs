using System.Globalization;

using CardPlatform.Authorization.Infrastructure;
using CardPlatform.Messaging;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.EndToEnd.Tests.Infrastructure;

/// <summary>
/// The real Authorization API (Development, so it migrates its database and creates its Kafka topics on startup) with
/// the outbox publisher and the consumer of <c>fraud-signals</c> enabled, as in the AppHost. Its Ledger client sends its
/// requests straight to the in-process <paramref name="ledger"/>.
/// </summary>
public sealed class AuthorizationApiFactory(
    string connectionString,
    string kafkaBootstrapServers,
    InProcessLedgerFactory ledger) : WebApplicationFactory<Program>
{
    // Address the Ledger client resolves for https+http://ledger-api; the test server accepts any host.
    private const string LedgerAddress = "http://ledger.test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{DependencyInjection.ConnectionName}", connectionString);
        builder.UseSetting($"ConnectionStrings:{MessagingHostingExtensions.DefaultKafkaConnectionName}", kafkaBootstrapServers);

        // What Aspire injects for .WithReference(ledgerApi).
        builder.UseSetting("services:ledger-api:http:0", LedgerAddress);

        // Faster than the defaults, so the test does not wait on polling or retries.
        builder.UseSetting("Messaging:Outbox:PollingInterval", "00:00:00.100");
        builder.UseSetting("Messaging:Consumer:RetryInitialDelay", "00:00:00.200");
        builder.UseSetting("Messaging:Consumer:RetryMaxDelay", "00:00:01");

        // Generous timeouts: the in-process Ledger may be slow on its first (cold) requests.
        builder.UseSetting("Ledger:AttemptTimeout", TimeSpan.FromSeconds(5).ToString("c", CultureInfo.InvariantCulture));
        builder.UseSetting("Ledger:TotalRequestTimeout", TimeSpan.FromSeconds(10).ToString("c", CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
            services.AddHttpClient(DependencyInjection.LedgerHttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => ledger.Server.CreateHandler()));
    }
}
