using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Idempotency;
using CardPlatform.Authorization.Infrastructure.Persistence;
using CardPlatform.Authorization.Infrastructure.Persistence.Idempotency;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Authorization.Api.Tests.Authorizations;

/// <summary>Idempotency of <c>POST /authorizations</c> (spec 006, ADR 0008).</summary>
public partial class AuthorizationEndpointsTests
{
    [Fact]
    public async Task Same_purchase_sent_twice_with_the_same_key_places_one_hold_and_returns_identical_responses()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        var purchase = new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" };
        string key = NewIdempotencyKey();

        HttpResponseMessage first = await PostAuthorizationAsync(purchase, key);
        HttpResponseMessage second = await PostAuthorizationAsync(purchase, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        string body = await first.Content.ReadAsStringAsync();
        Assert.Equal(body, await second.Content.ReadAsStringAsync());
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(first.Content.Headers.ContentType, second.Content.Headers.ContentType);

        JsonElement authorization = JsonDocument.Parse(body).RootElement;
        Assert.Equal("approved", authorization.GetProperty("status").GetString());
        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));
        Assert.Equal(1, await CountAuthorizationsAsync(cardId));
    }

    [Fact]
    public async Task Same_key_with_an_equivalent_body_returns_the_recorded_response()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        Guid merchantAccountId = Guid.NewGuid();
        string key = NewIdempotencyKey();

        HttpResponseMessage first = await PostAuthorizationAsync(
            new { cardId, merchantAccountId, amount = 30m, currency = "BRL" },
            key);

        // Other property order, whitespace, GUID case and trailing zeros: the same request.
        string equivalent =
            $"{{ \"currency\" : \"BRL\",\n \"amount\": 30.00, \"merchantAccountId\": \"{merchantAccountId.ToString().ToUpperInvariant()}\", \"cardId\": \"{cardId}\" }}";
        HttpResponseMessage second = await PostRawAuthorizationAsync(equivalent, key);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));
    }

    [Fact]
    public async Task Same_key_with_a_different_body_returns_unprocessable_entity_with_idempotency_key_reused()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        Guid merchantAccountId = Guid.NewGuid();
        string key = NewIdempotencyKey();
        Assert.Equal(
            HttpStatusCode.Created,
            (await PostAuthorizationAsync(new { cardId, merchantAccountId, amount = 30m, currency = "BRL" }, key)).StatusCode);

        HttpResponseMessage response = await PostAuthorizationAsync(
            new { cardId, merchantAccountId, amount = 31m, currency = "BRL" },
            key);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "idempotency_key_reused");
        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));
        Assert.Equal(1, await CountAuthorizationsAsync(cardId));
    }

    [Fact]
    public async Task Concurrent_requests_with_the_same_key_create_one_authorization_and_one_hold()
    {
        const int requests = 16;
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        var purchase = new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" };
        string key = NewIdempotencyKey();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, requests).Select(_ => Task.Run(() => PostAuthorizationAsync(purchase, key))));

        // Every request either got the recorded response or was told the first one was still in progress.
        HttpResponseMessage[] created = [.. responses.Where(static response => response.StatusCode == HttpStatusCode.Created)];
        Assert.NotEmpty(created);
        string body = await created[0].Content.ReadAsStringAsync();
        foreach (HttpResponseMessage response in created)
        {
            Assert.Equal(body, await response.Content.ReadAsStringAsync());
        }

        foreach (HttpResponseMessage response in responses.Except(created))
        {
            await AssertProblemAsync(response, HttpStatusCode.Conflict, "idempotency_request_in_progress");
        }

        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));
        Assert.Equal(1, await CountAuthorizationsAsync(cardId));

        // Once everything finished, the key answers with the recorded response.
        HttpResponseMessage replay = await PostAuthorizationAsync(purchase, key);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(body, await replay.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_or_empty_idempotency_key_returns_bad_request_with_idempotency_key_required(string? key)
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        using HttpRequestMessage request = new(HttpMethod.Post, "/authorizations")
        {
            Content = JsonContent.Create(new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" }),
        };
        if (key is not null)
        {
            Assert.True(request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, key));
        }

        HttpResponseMessage response = await _client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "idempotency_key_required");
        Assert.Equal(0, await CountAuthorizationsAsync(cardId));
        Assert.Equal(100m, await GetLedgerBalanceAsync(cardAccountId));
    }

    [Fact]
    public async Task Too_long_idempotency_key_returns_bad_request_with_invalid_idempotency_key()
    {
        (Guid cardId, _) = await CreateFundedCardAsync(100m);

        HttpResponseMessage response = await PostAuthorizationAsync(
            new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" },
            new string('k', IdempotentAuthorizePurchaseHandler.MaxKeyLength + 1));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "invalid_idempotency_key");
    }

    [Fact]
    public async Task Service_unavailable_is_not_stored_and_the_same_key_is_processed_once_the_ledger_is_back()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        var purchase = new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" };
        string key = NewIdempotencyKey();
        using WebApplicationFactory<Program> ledgerDown = factory.WithLedgerDown();

        HttpResponseMessage unavailable = await PostAuthorizationAsync(purchase, key, ledgerDown.CreateClient());

        await AssertProblemAsync(unavailable, HttpStatusCode.ServiceUnavailable, "ledger_unavailable");
        IdempotencyKeyRecord reserved = await FindIdempotencyKeyAsync(key);
        Assert.Null(reserved.ResponseStatusCode);
        Assert.Null(reserved.ResponseBody);
        Assert.Null(reserved.LockedUntil);

        HttpResponseMessage response = await PostAuthorizationAsync(purchase, key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement authorization = await ReadJsonAsync(response);
        Assert.Equal("approved", authorization.GetProperty("status").GetString());
        Assert.Equal(reserved.AuthorizationId, authorization.GetProperty("id").GetGuid());
        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));
        Assert.Equal(1, await CountAuthorizationsAsync(cardId));
    }

    [Fact]
    public async Task Hold_placed_but_reply_lost_is_reused_by_the_retry_with_the_same_key()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        var purchase = new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" };
        string key = NewIdempotencyKey();
        using WebApplicationFactory<Program> replyLost = factory.WithLedgerReplyLost();

        HttpResponseMessage unavailable = await PostAuthorizationAsync(purchase, key, replyLost.CreateClient());

        // The Ledger placed the hold under the id reserved for the key, but the Authorization never heard back.
        await AssertProblemAsync(unavailable, HttpStatusCode.ServiceUnavailable, "ledger_unavailable");
        Guid reservedId = (await FindIdempotencyKeyAsync(key)).AuthorizationId;
        Assert.Equal(HttpStatusCode.OK, (await _ledger.GetAsync($"/holds/{reservedId}")).StatusCode);
        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));
        Assert.Equal(0, await CountAuthorizationsAsync(cardId));

        HttpResponseMessage response = await PostAuthorizationAsync(purchase, key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement authorization = await ReadJsonAsync(response);
        Assert.Equal("approved", authorization.GetProperty("status").GetString());
        Assert.Equal(reservedId, authorization.GetProperty("id").GetGuid());
        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));
        Assert.Equal(1, await CountAuthorizationsAsync(cardId));
    }

    [Fact]
    public async Task Key_claimed_by_a_crashed_request_is_in_progress_until_its_lease_expires_then_processed_with_its_id()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        Guid merchantAccountId = Guid.NewGuid();
        var purchase = new { cardId, merchantAccountId, amount = 30m, currency = "BRL" };
        string key = NewIdempotencyKey();
        Guid reservedId = Guid.CreateVersion7();
        await InsertIdempotencyKeyAsync(new IdempotencyKeyRecord
        {
            Key = key,
            RequestHash = PurchaseRequestHash.Compute(new AuthorizePurchase(cardId, merchantAccountId, 30m, "BRL")),
            AuthorizationId = reservedId,
            ClaimToken = Guid.NewGuid(),
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await AssertProblemAsync(
            await PostAuthorizationAsync(purchase, key),
            HttpStatusCode.Conflict,
            "idempotency_request_in_progress");

        await ExpireLeaseAsync(key);
        HttpResponseMessage response = await PostAuthorizationAsync(purchase, key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(reservedId, (await ReadJsonAsync(response)).GetProperty("id").GetGuid());
        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));
    }

    [Fact]
    public async Task Rejected_request_does_not_bind_the_key_to_its_body()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        Guid merchantAccountId = Guid.NewGuid();
        string key = NewIdempotencyKey();

        await AssertProblemAsync(
            await PostAuthorizationAsync(new { cardId, merchantAccountId, amount = 0m, currency = "BRL" }, key),
            HttpStatusCode.BadRequest,
            "invalid_amount");
        await AssertProblemAsync(
            await PostAuthorizationAsync(new { cardId = Guid.NewGuid(), merchantAccountId, amount = 30m, currency = "BRL" }, key),
            HttpStatusCode.NotFound,
            "card_not_found");
        HttpResponseMessage response = await PostAuthorizationAsync(
            new { cardId, merchantAccountId, amount = 30m, currency = "BRL" },
            key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));
    }

    [Fact]
    public async Task Declined_purchase_is_replayed_too()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(20m);
        var purchase = new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" };
        string key = NewIdempotencyKey();

        HttpResponseMessage first = await PostAuthorizationAsync(purchase, key);
        HttpResponseMessage second = await PostAuthorizationAsync(purchase, key);

        Guid id = await AssertDeclinedAsync(first, "insufficient_funds");
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal($"/authorizations/{id}", second.Headers.Location?.OriginalString);
        Assert.Equal(1, await CountAuthorizationsAsync(cardId));
        Assert.Equal(20m, await GetLedgerBalanceAsync(cardAccountId));
    }

    private async Task<HttpResponseMessage> PostRawAuthorizationAsync(string json, string idempotencyKey)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/authorizations")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(IdempotencyKeyHeader, idempotencyKey);
        return await _client.SendAsync(request);
    }

    private async Task<int> CountAuthorizationsAsync(Guid cardId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        return await dbContext.Authorizations.CountAsync(authorization => authorization.CardId == cardId);
    }

    private async Task<IdempotencyKeyRecord> FindIdempotencyKeyAsync(string key)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        return await dbContext.IdempotencyKeys.AsNoTracking().SingleAsync(record => record.Key == key);
    }

    private async Task InsertIdempotencyKeyAsync(IdempotencyKeyRecord record)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        dbContext.IdempotencyKeys.Add(record);
        await dbContext.SaveChangesAsync();
    }

    private async Task ExpireLeaseAsync(string key)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        await dbContext.IdempotencyKeys
            .Where(record => record.Key == key)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                record => record.LockedUntil,
                (DateTimeOffset?)DateTimeOffset.UtcNow.AddSeconds(-1)));
    }
}
