using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

namespace CardPlatform.Authorization.Api.Tests.Authorizations;

// Capture and void of authorizations (spec 007): the Ledger moves the held money and the authorization follows it.
public partial class AuthorizationEndpointsTests
{
    [Fact]
    public async Task Capture_moves_the_held_amount_to_the_merchant_and_marks_the_authorization_captured()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);

        HttpResponseMessage response = await _client.PostAsync($"/authorizations/{purchase.Id}/capture", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement authorization = await ReadJsonAsync(response);
        Assert.Equal(purchase.Id, authorization.GetProperty("id").GetGuid());
        Assert.Equal("captured", authorization.GetProperty("status").GetString());
        Assert.Equal(purchase.MerchantAccountId, authorization.GetProperty("merchantAccountId").GetGuid());
        Assert.Equal(30m, authorization.GetProperty("amount").GetDecimal());
        Assert.NotEqual(default, authorization.GetProperty("completedAt").GetDateTimeOffset());

        Assert.Equal(30m, await GetLedgerBalanceAsync(purchase.MerchantAccountId));
        Assert.Equal(70m, await GetLedgerBalanceAsync(purchase.CardAccountId));
        Assert.Equal("captured", await GetHoldStatusAsync(purchase.Id));
        await AssertRecordedAsync(purchase.Id, "captured", reason: null);
    }

    [Fact]
    public async Task Void_returns_the_held_amount_to_the_card_and_marks_the_authorization_voided()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        Assert.Equal(70m, await GetLedgerBalanceAsync(purchase.CardAccountId));

        HttpResponseMessage response = await _client.PostAsync($"/authorizations/{purchase.Id}/void", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement authorization = await ReadJsonAsync(response);
        Assert.Equal(purchase.Id, authorization.GetProperty("id").GetGuid());
        Assert.Equal("voided", authorization.GetProperty("status").GetString());
        Assert.NotEqual(default, authorization.GetProperty("completedAt").GetDateTimeOffset());

        Assert.Equal(100m, await GetLedgerBalanceAsync(purchase.CardAccountId));
        Assert.Equal(0m, await GetLedgerBalanceAsync(purchase.MerchantAccountId));
        Assert.Equal("voided", await GetHoldStatusAsync(purchase.Id));
        await AssertRecordedAsync(purchase.Id, "voided", reason: null);
    }

    [Fact]
    public async Task Capturing_twice_returns_not_capturable_and_moves_the_money_once()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(purchase.Id, "capture")).StatusCode);

        HttpResponseMessage response = await CompleteAsync(purchase.Id, "capture");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "authorization_not_capturable");
        Assert.Equal(30m, await GetLedgerBalanceAsync(purchase.MerchantAccountId));
        Assert.Equal(70m, await GetLedgerBalanceAsync(purchase.CardAccountId));
        await AssertRecordedAsync(purchase.Id, "captured", reason: null);
    }

    [Fact]
    public async Task Capturing_after_a_void_returns_not_capturable_and_leaves_the_balances_unchanged()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(purchase.Id, "void")).StatusCode);

        HttpResponseMessage response = await CompleteAsync(purchase.Id, "capture");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "authorization_not_capturable");
        Assert.Equal(100m, await GetLedgerBalanceAsync(purchase.CardAccountId));
        Assert.Equal(0m, await GetLedgerBalanceAsync(purchase.MerchantAccountId));
        await AssertRecordedAsync(purchase.Id, "voided", reason: null);
    }

    [Theory]
    [InlineData("capture", "authorization_not_capturable")]
    [InlineData("void", "authorization_not_voidable")]
    public async Task Capturing_or_voiding_a_declined_authorization_returns_unprocessable_and_leaves_the_balances_unchanged(
        string operation,
        string code)
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(20m);
        Guid merchantAccountId = await OpenLedgerAccountAsync("merchant");
        Guid id = await AssertDeclinedAsync(
            await AuthorizeAsync(cardId, merchantAccountId, 30m, "BRL"), "insufficient_funds");

        HttpResponseMessage response = await CompleteAsync(id, operation);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, code);
        Assert.Equal(20m, await GetLedgerBalanceAsync(cardAccountId));
        Assert.Equal(0m, await GetLedgerBalanceAsync(merchantAccountId));
        await AssertRecordedAsync(id, "declined", "insufficient_funds");
    }

    [Fact]
    public async Task Voiding_twice_returns_not_voidable_and_returns_the_money_once()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(purchase.Id, "void")).StatusCode);

        HttpResponseMessage response = await CompleteAsync(purchase.Id, "void");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "authorization_not_voidable");
        Assert.Equal(100m, await GetLedgerBalanceAsync(purchase.CardAccountId));
        await AssertRecordedAsync(purchase.Id, "voided", reason: null);
    }

    [Fact]
    public async Task Voiding_after_a_capture_returns_not_voidable_and_leaves_the_balances_unchanged()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(purchase.Id, "capture")).StatusCode);

        HttpResponseMessage response = await CompleteAsync(purchase.Id, "void");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "authorization_not_voidable");
        Assert.Equal(70m, await GetLedgerBalanceAsync(purchase.CardAccountId));
        Assert.Equal(30m, await GetLedgerBalanceAsync(purchase.MerchantAccountId));
        await AssertRecordedAsync(purchase.Id, "captured", reason: null);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("void")]
    public async Task Capturing_or_voiding_an_unknown_authorization_returns_not_found(string operation)
    {
        HttpResponseMessage response = await CompleteAsync(Guid.NewGuid(), operation);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "authorization_not_found");
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("void")]
    public async Task Ledger_down_returns_service_unavailable_and_leaves_the_authorization_approved(string operation)
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        using WebApplicationFactory<Program> ledgerDown = factory.WithLedgerDown();

        HttpResponseMessage response = await CompleteAsync(purchase.Id, operation, ledgerDown.CreateClient());

        await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "ledger_unavailable");
        await AssertRecordedAsync(purchase.Id, "approved", reason: null);
        Assert.Equal("pending", await GetHoldStatusAsync(purchase.Id));
        Assert.Equal(70m, await GetLedgerBalanceAsync(purchase.CardAccountId));
        Assert.Equal(0m, await GetLedgerBalanceAsync(purchase.MerchantAccountId));
    }

    [Fact]
    public async Task Capture_to_an_unknown_merchant_account_returns_the_ledger_error_and_leaves_the_authorization_approved()
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(100m);
        JsonElement created = await ReadJsonAsync(await AuthorizeAsync(cardId, Guid.NewGuid(), 30m, "BRL"));
        Guid id = created.GetProperty("id").GetGuid();

        HttpResponseMessage response = await CompleteAsync(id, "capture");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "account_not_found");
        await AssertRecordedAsync(id, "approved", reason: null);
        Assert.Equal("pending", await GetHoldStatusAsync(id));
        Assert.Equal(70m, await GetLedgerBalanceAsync(cardAccountId));
    }

    [Fact]
    public async Task Capture_whose_reply_was_lost_is_reconciled_by_the_next_capture_and_moves_the_money_once()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        using WebApplicationFactory<Program> replyLost = factory.WithLedgerReplyLost(
            static request => request.RequestUri?.AbsolutePath.EndsWith("/capture", StringComparison.Ordinal) == true);

        HttpResponseMessage lost = await CompleteAsync(purchase.Id, "capture", replyLost.CreateClient());

        // The Ledger captured the hold, but the Authorization did not hear back: unknown outcome, still approved.
        await AssertProblemAsync(lost, HttpStatusCode.ServiceUnavailable, "ledger_unavailable");
        await AssertRecordedAsync(purchase.Id, "approved", reason: null);
        Assert.Equal("captured", await GetHoldStatusAsync(purchase.Id));

        HttpResponseMessage response = await CompleteAsync(purchase.Id, "capture");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("captured", (await ReadJsonAsync(response)).GetProperty("status").GetString());
        await AssertRecordedAsync(purchase.Id, "captured", reason: null);
        Assert.Equal(30m, await GetLedgerBalanceAsync(purchase.MerchantAccountId));
        Assert.Equal(70m, await GetLedgerBalanceAsync(purchase.CardAccountId));
    }

    [Fact]
    public async Task Void_of_a_hold_already_voided_in_the_ledger_is_reconciled_as_voided()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        Assert.Equal(HttpStatusCode.OK, (await _ledger.PostAsync($"/holds/{purchase.Id}/void", content: null)).StatusCode);

        HttpResponseMessage response = await CompleteAsync(purchase.Id, "void");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("voided", (await ReadJsonAsync(response)).GetProperty("status").GetString());
        await AssertRecordedAsync(purchase.Id, "voided", reason: null);
        Assert.Equal(100m, await GetLedgerBalanceAsync(purchase.CardAccountId));
    }

    [Fact]
    public async Task Capture_of_a_hold_already_voided_in_the_ledger_returns_not_capturable_and_marks_the_authorization_voided()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        Assert.Equal(HttpStatusCode.OK, (await _ledger.PostAsync($"/holds/{purchase.Id}/void", content: null)).StatusCode);

        HttpResponseMessage response = await CompleteAsync(purchase.Id, "capture");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "authorization_not_capturable");
        await AssertRecordedAsync(purchase.Id, "voided", reason: null);
        Assert.Equal(100m, await GetLedgerBalanceAsync(purchase.CardAccountId));
        Assert.Equal(0m, await GetLedgerBalanceAsync(purchase.MerchantAccountId));
    }

    [Fact]
    public async Task Void_of_a_hold_already_captured_in_the_ledger_returns_not_voidable_and_marks_the_authorization_captured()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        HttpResponseMessage captured = await _ledger.PostAsJsonAsync(
            $"/holds/{purchase.Id}/capture",
            new { merchantAccountId = purchase.MerchantAccountId });
        Assert.Equal(HttpStatusCode.OK, captured.StatusCode);

        HttpResponseMessage response = await CompleteAsync(purchase.Id, "void");

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "authorization_not_voidable");
        await AssertRecordedAsync(purchase.Id, "captured", reason: null);
        Assert.Equal(70m, await GetLedgerBalanceAsync(purchase.CardAccountId));
        Assert.Equal(30m, await GetLedgerBalanceAsync(purchase.MerchantAccountId));
    }

    /// <summary>An approved purchase of <paramref name="amount"/> on a card funded with <paramref name="balance"/>.</summary>
    private async Task<Purchase> AuthorizeApprovedAsync(decimal balance, decimal amount)
    {
        (Guid cardId, Guid cardAccountId) = await CreateFundedCardAsync(balance);
        Guid merchantAccountId = await OpenLedgerAccountAsync("merchant");

        HttpResponseMessage response = await AuthorizeAsync(cardId, merchantAccountId, amount, "BRL");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement authorization = await ReadJsonAsync(response);
        Assert.Equal("approved", authorization.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, authorization.GetProperty("completedAt").ValueKind);
        return new Purchase(authorization.GetProperty("id").GetGuid(), cardAccountId, merchantAccountId);
    }

    /// <summary>Sends <c>POST /authorizations/{id}/{operation}</c> (capture or void) through this API by default.</summary>
    private Task<HttpResponseMessage> CompleteAsync(Guid id, string operation, HttpClient? client = null)
    {
        return (client ?? _client).PostAsync($"/authorizations/{id}/{operation}", content: null);
    }

    private async Task<string?> GetHoldStatusAsync(Guid holdId)
    {
        HttpResponseMessage response = await _ledger.GetAsync($"/holds/{holdId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("status").GetString();
    }

    private sealed record Purchase(Guid Id, Guid CardAccountId, Guid MerchantAccountId);
}
