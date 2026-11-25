using System.Net;

using CardPlatform.Authorization.Api.Tests.Infrastructure;

namespace CardPlatform.Authorization.Api.Tests;

[Collection(AuthorizationApiCollectionFixture.Name)]
public class HealthCheckTests(AuthorizationApiFactory factory)
{
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
