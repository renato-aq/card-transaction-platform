extern alias LedgerApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CardPlatform.EndToEnd.Tests.Infrastructure;

/// <summary>
/// Runs the real Ledger API in process (Development, so it creates and migrates its database on startup) against its own
/// database. The Authorization's Ledger client reaches it through <see cref="WebApplicationFactory{TEntryPoint}.Server"/>.
/// </summary>
public sealed class InProcessLedgerFactory(string connectionString) : WebApplicationFactory<LedgerApi::Program>
{
    public const string ConnectionName = "ledgerdb";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"ConnectionStrings:{ConnectionName}", connectionString);
    }
}
