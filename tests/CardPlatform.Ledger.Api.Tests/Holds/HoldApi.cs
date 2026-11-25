using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using CardPlatform.Ledger.Api.Tests.Infrastructure;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;
using CardPlatform.Ledger.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Ledger.Api.Tests.Holds;

/// <summary>Calls and checks shared by the hold endpoint tests.</summary>
internal sealed class HoldApi(LedgerApiFactory factory, HttpClient client)
{
    public const string ProblemJson = "application/problem+json";

    public HttpClient Client => client;

    public async Task<Guid> OpenAccountAsync(string type, string currency = "BRL")
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/accounts", new { type, currency });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>Opens a settlement and a card account and tops the card up by <paramref name="amount"/>.</summary>
    public async Task<(Guid Settlement, Guid Card)> OpenFundedCardAsync(decimal amount, string currency = "BRL")
    {
        Guid settlement = await OpenAccountAsync("settlement", currency);
        Guid card = await OpenAccountAsync("card", currency);
        HttpResponseMessage response = await client.PostAsJsonAsync("/transactions", new
        {
            currency,
            entries = new[]
            {
                new { accountId = settlement, direction = "debit", amount },
                new { accountId = card, direction = "credit", amount },
            },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (settlement, card);
    }

    public Task<HttpResponseMessage> PlaceAsync(Guid? id, Guid? cardAccountId, decimal? amount, string? currency = "BRL")
    {
        return client.PostAsJsonAsync("/holds", new { id, cardAccountId, amount, currency });
    }

    /// <summary>Places a new hold and checks it was created.</summary>
    public async Task<Guid> PlaceCreatedAsync(Guid cardAccountId, decimal amount, string currency = "BRL")
    {
        Guid id = Guid.CreateVersion7();
        HttpResponseMessage response = await PlaceAsync(id, cardAccountId, amount, currency);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return id;
    }

    public Task<HttpResponseMessage> CaptureAsync(Guid holdId, Guid? merchantAccountId)
    {
        return client.PostAsJsonAsync($"/holds/{holdId}/capture", new { merchantAccountId });
    }

    public Task<HttpResponseMessage> VoidAsync(Guid holdId)
    {
        return client.PostAsync($"/holds/{holdId}/void", content: null);
    }

    public async Task<decimal> GetBalanceAsync(Guid accountId)
    {
        HttpResponseMessage response = await client.GetAsync($"/accounts/{accountId}/balance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("balance").GetProperty("amount").GetDecimal();
    }

    public async Task<JsonElement> GetHoldAsync(Guid holdId)
    {
        HttpResponseMessage response = await client.GetAsync($"/holds/{holdId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public static async Task<string?> ReadCodeAsync(HttpResponseMessage response)
    {
        return (await ReadJsonAsync(response)).GetProperty("code").GetString();
    }

    public static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal((int)expectedStatus, problem.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
    }

    public static void AssertHold(
        JsonElement hold,
        Guid id,
        Guid cardAccountId,
        decimal amount,
        string currency,
        string status)
    {
        Assert.Equal(id, hold.GetProperty("id").GetGuid());
        Assert.Equal(cardAccountId, hold.GetProperty("cardAccountId").GetGuid());
        Assert.Equal(JsonValueKind.Number, hold.GetProperty("amount").ValueKind);
        Assert.Equal(amount, hold.GetProperty("amount").GetDecimal());
        Assert.Equal(currency, hold.GetProperty("currency").GetString());
        Assert.Equal(status, hold.GetProperty("status").GetString());
        Assert.True(hold.GetProperty("createdAt").TryGetDateTimeOffset(out _));
        if (status == "pending")
        {
            Assert.Equal(JsonValueKind.Null, hold.GetProperty("completedAt").ValueKind);
        }
        else
        {
            Assert.True(hold.GetProperty("completedAt").TryGetDateTimeOffset(out _));
        }
    }

    // The holds account is a system account with no endpoint to find it, so it is read from the database.
    public async Task<IReadOnlyList<Guid>> FindHoldsAccountsAsync(string currencyCode)
    {
        Currency currency = Currency.Create(currencyCode).Value;
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        LedgerDbContext dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        return await dbContext.Accounts
            .Where(account => account.Type == AccountType.Holds && account.Currency == currency)
            .Select(static account => account.Id)
            .ToListAsync();
    }

    public async Task<Guid> GetHoldsAccountAsync(string currencyCode)
    {
        return Assert.Single(await FindHoldsAccountsAsync(currencyCode));
    }

    // Reads the database directly: there is no endpoint to list transactions or holds.
    public async Task<int> CountEntriesAsync(Guid accountId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        LedgerDbContext dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        return await dbContext.Set<Entry>().CountAsync(entry => entry.AccountId == accountId);
    }

    public async Task<int> CountHoldsAsync(Guid cardAccountId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        LedgerDbContext dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        return await dbContext.Set<Hold>().CountAsync(hold => hold.CardAccountId == cardAccountId);
    }
}
