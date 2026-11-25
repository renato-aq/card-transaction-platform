using System.Net;
using System.Text.Json;

using CardPlatform.Authorization.Api.Tests.Infrastructure;

using Microsoft.AspNetCore.Mvc.Testing;

namespace CardPlatform.Authorization.Api.Tests.Authorizations;

// A clock finer than PostgreSQL (100 ns ticks on Linux, whole microseconds in timestamptz): the instants returned right
// after a write, and replayed from the idempotency key, are the ones read back from the database.
public partial class AuthorizationEndpointsTests
{
    [Fact]
    public async Task Created_at_is_the_same_in_the_purchase_response_its_replay_and_the_recorded_authorization()
    {
        using WebApplicationFactory<Program> api = factory.WithClock(new SubMicrosecondTimeProvider());
        HttpClient client = api.CreateClient();
        (Guid cardId, _) = await CreateFundedCardAsync(100m);
        object body = new { cardId, merchantAccountId = Guid.NewGuid(), amount = 10m, currency = "BRL" };
        string key = NewIdempotencyKey();

        HttpResponseMessage created = await PostAuthorizationAsync(body, key, client);
        HttpResponseMessage replayed = await PostAuthorizationAsync(body, key, client);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        JsonElement authorization = await ReadJsonAsync(created);
        string? createdAt = authorization.GetProperty("createdAt").GetString();
        Assert.Equal(createdAt, (await ReadJsonAsync(replayed)).GetProperty("createdAt").GetString());
        JsonElement recorded = await ReadJsonAsync(
            await client.GetAsync($"/authorizations/{authorization.GetProperty("id").GetGuid()}"));
        Assert.Equal(createdAt, recorded.GetProperty("createdAt").GetString());
    }

    [Fact]
    public async Task Completed_at_is_the_same_in_the_capture_response_and_the_recorded_authorization()
    {
        using WebApplicationFactory<Program> api = factory.WithClock(new SubMicrosecondTimeProvider());
        HttpClient client = api.CreateClient();
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);

        HttpResponseMessage captured = await CompleteAsync(purchase.Id, "capture", client);

        Assert.Equal(HttpStatusCode.OK, captured.StatusCode);
        string? completedAt = (await ReadJsonAsync(captured)).GetProperty("completedAt").GetString();
        JsonElement recorded = await ReadJsonAsync(await client.GetAsync($"/authorizations/{purchase.Id}"));
        Assert.Equal(completedAt, recorded.GetProperty("completedAt").GetString());
    }
}
