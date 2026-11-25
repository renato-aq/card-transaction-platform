using System.Net;

using CardPlatform.Ledger.Api.Tests.Infrastructure;

namespace CardPlatform.Ledger.Api.Tests;

[Collection(LedgerApiCollectionFixture.Name)]
public class HealthCheckTests(LedgerApiFactory factory)
{
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
