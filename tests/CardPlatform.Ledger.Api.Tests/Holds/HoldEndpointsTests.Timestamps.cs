using System.Net;

using CardPlatform.Ledger.Api.Tests.Infrastructure;

using Microsoft.AspNetCore.Mvc.Testing;

namespace CardPlatform.Ledger.Api.Tests.Holds;

// A clock finer than PostgreSQL (100 ns ticks on Linux, whole microseconds in timestamptz): the instants returned right
// after a write are the ones a repeated place or a later read get back from the database.
public partial class HoldEndpointsTests
{
    [Fact]
    public async Task Created_at_is_the_same_in_the_place_response_its_repeat_and_the_recorded_hold()
    {
        await using WebApplicationFactory<Program> ledger = factory.WithClock(new SubMicrosecondTimeProvider());
        HoldApi api = new(factory, ledger.CreateClient());
        (_, Guid card) = await api.OpenFundedCardAsync(100m);
        Guid id = Guid.CreateVersion7();

        HttpResponseMessage created = await api.PlaceAsync(id, card, 25m);
        HttpResponseMessage repeated = await api.PlaceAsync(id, card, 25m);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        string? createdAt = (await HoldApi.ReadJsonAsync(created)).GetProperty("createdAt").GetString();
        Assert.Equal(createdAt, (await HoldApi.ReadJsonAsync(repeated)).GetProperty("createdAt").GetString());
        Assert.Equal(createdAt, (await api.GetHoldAsync(id)).GetProperty("createdAt").GetString());
    }

    [Fact]
    public async Task Completed_at_is_the_same_in_the_capture_and_void_responses_and_the_recorded_holds()
    {
        await using WebApplicationFactory<Program> ledger = factory.WithClock(new SubMicrosecondTimeProvider());
        HoldApi api = new(factory, ledger.CreateClient());
        (_, Guid card) = await api.OpenFundedCardAsync(100m);
        Guid merchant = await api.OpenAccountAsync("merchant");
        Guid captured = await api.PlaceCreatedAsync(card, 30m);
        Guid voided = await api.PlaceCreatedAsync(card, 20m);

        HttpResponseMessage captureResponse = await api.CaptureAsync(captured, merchant);
        HttpResponseMessage voidResponse = await api.VoidAsync(voided);

        Assert.Equal(HttpStatusCode.OK, captureResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);
        Assert.Equal(
            (await HoldApi.ReadJsonAsync(captureResponse)).GetProperty("completedAt").GetString(),
            (await api.GetHoldAsync(captured)).GetProperty("completedAt").GetString());
        Assert.Equal(
            (await HoldApi.ReadJsonAsync(voidResponse)).GetProperty("completedAt").GetString(),
            (await api.GetHoldAsync(voided)).GetProperty("completedAt").GetString());
    }
}
