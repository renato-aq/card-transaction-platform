using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Monetary;
using CardPlatform.Authorization.Infrastructure.Persistence;
using CardPlatform.Contracts;
using CardPlatform.Contracts.Authorizations;
using CardPlatform.Messaging.Outbox;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Authorization.Api.Tests.Authorizations;

/// <summary>
/// The transactional outbox of the Authorization (spec 001 of the outbox task): every decision is written to
/// <c>outbox_messages</c> in the same transaction as the authorization.
/// </summary>
public partial class AuthorizationEndpointsTests
{
    [Fact]
    public async Task Approved_purchase_writes_exactly_one_authorization_approved_event_with_the_authorization_data()
    {
        (Guid cardId, _) = await CreateFundedCardAsync(100m);
        Guid merchantAccountId = await OpenLedgerAccountAsync("merchant");

        JsonElement authorization = await ReadJsonAsync(await AuthorizeAsync(cardId, merchantAccountId, 30m, "BRL"));

        Guid id = authorization.GetProperty("id").GetGuid();
        DateTimeOffset createdAt = authorization.GetProperty("createdAt").GetDateTimeOffset();
        OutboxEvent outboxEvent = Assert.Single(await ReadOutboxAsync(cardId));
        Assert.Equal(new AuthorizationApproved(id, cardId, merchantAccountId, 30m, "BRL", createdAt), outboxEvent.Event);
        AssertRow(outboxEvent, EventTypes.AuthorizationApproved, cardId, occurredAt: createdAt);
        Assert.Equal(AuthorizationEvents.EventIdOf(id, typeof(AuthorizationApproved)), outboxEvent.Message.EventId);
    }

    [Theory]
    [InlineData("insufficient_funds")]
    [InlineData("card_blocked")]
    [InlineData("currency_mismatch")]
    public async Task Declined_purchase_writes_an_authorization_declined_event_with_the_reason(string reason)
    {
        (Guid cardId, _) = await CreateFundedCardAsync(reason == "insufficient_funds" ? 20m : 100m);
        if (reason == "card_blocked")
        {
            Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/cards/{cardId}/block", content: null)).StatusCode);
        }

        Guid merchantAccountId = Guid.NewGuid();
        string currency = reason == "currency_mismatch" ? "USD" : "BRL";

        HttpResponseMessage response = await AuthorizeAsync(cardId, merchantAccountId, 30m, currency);

        Guid id = await AssertDeclinedAsync(response, reason);
        DateTimeOffset createdAt = (await ReadJsonAsync(response)).GetProperty("createdAt").GetDateTimeOffset();
        OutboxEvent outboxEvent = Assert.Single(await ReadOutboxAsync(cardId));
        Assert.Equal(
            new AuthorizationDeclined(id, cardId, merchantAccountId, 30m, currency, createdAt, reason),
            outboxEvent.Event);
        AssertRow(outboxEvent, EventTypes.AuthorizationDeclined, cardId, occurredAt: createdAt);
    }

    [Fact]
    public async Task Repeating_the_same_idempotency_key_does_not_write_another_event()
    {
        (Guid cardId, _) = await CreateFundedCardAsync(100m);
        var purchase = new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" };
        string key = NewIdempotencyKey();

        Assert.Equal(HttpStatusCode.Created, (await PostAuthorizationAsync(purchase, key)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAuthorizationAsync(purchase, key)).StatusCode);

        Assert.IsType<AuthorizationApproved>(Assert.Single(await ReadOutboxAsync(cardId)).Event);
    }

    [Fact]
    public async Task Ledger_down_writes_no_event_and_the_retry_with_the_same_key_writes_one()
    {
        (Guid cardId, _) = await CreateFundedCardAsync(100m);
        var purchase = new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" };
        string key = NewIdempotencyKey();
        using WebApplicationFactory<Program> ledgerDown = factory.WithLedgerDown();

        HttpResponseMessage unavailable = await PostAuthorizationAsync(purchase, key, ledgerDown.CreateClient());

        await AssertProblemAsync(unavailable, HttpStatusCode.ServiceUnavailable, "ledger_unavailable");
        Assert.Empty(await ReadOutboxAsync(cardId));

        Assert.Equal(HttpStatusCode.Created, (await PostAuthorizationAsync(purchase, key)).StatusCode);
        Assert.IsType<AuthorizationApproved>(Assert.Single(await ReadOutboxAsync(cardId)).Event);
    }

    [Fact]
    public async Task Rejected_purchase_writes_no_event()
    {
        (Guid cardId, _) = await CreateFundedCardAsync(100m);

        await AssertProblemAsync(
            await PostAuthorizationAsync(new { cardId, merchantAccountId = Guid.NewGuid(), amount = 0m, currency = "BRL" }),
            HttpStatusCode.BadRequest,
            "invalid_amount");

        Assert.Empty(await ReadOutboxAsync(cardId));
    }

    [Fact]
    public async Task Failed_save_of_the_authorization_writes_no_event()
    {
        (Guid cardId, _) = await CreateFundedCardAsync(100m);
        Guid merchantAccountId = Guid.NewGuid();
        string key = NewIdempotencyKey();
        Guid? reservedId = null;

        // While the hold is being placed, another request saves an authorization with the same reserved id, so this
        // request's INSERT into authorizations violates the primary key and its transaction is rolled back.
        using WebApplicationFactory<Program> concurrentSave = factory.WithBeforeLedgerRequest(async request =>
        {
            if (reservedId is null && request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/holds")
            {
                reservedId = (await FindIdempotencyKeyAsync(key)).AuthorizationId;
                await InsertApprovedAuthorizationAsync(reservedId.Value, cardId, merchantAccountId);
            }
        });

        HttpResponseMessage response = await PostAuthorizationAsync(
            new { cardId, merchantAccountId, amount = 30m, currency = "BRL" },
            key,
            concurrentSave.CreateClient());

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "idempotency_request_in_progress");
        Assert.NotNull(reservedId);
        Assert.Equal(1, await CountAuthorizationsAsync(cardId));
        Assert.Empty(await ReadOutboxAsync(cardId));
        Assert.Null((await FindIdempotencyKeyAsync(key)).ResponseStatusCode);
    }

    [Fact]
    public async Task Lost_idempotency_claim_writes_neither_the_authorization_nor_the_event()
    {
        (Guid cardId, _) = await CreateFundedCardAsync(100m);
        string key = NewIdempotencyKey();

        // While the hold is being placed, another request takes the key over: completing it fails, and the whole
        // transaction (authorization, key response and event) is rolled back.
        using WebApplicationFactory<Program> claimLost = factory.WithBeforeLedgerRequest(async request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/holds")
            {
                await TakeIdempotencyClaimOverAsync(key);
            }
        });

        HttpResponseMessage response = await PostAuthorizationAsync(
            new { cardId, merchantAccountId = Guid.NewGuid(), amount = 30m, currency = "BRL" },
            key,
            claimLost.CreateClient());

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "idempotency_request_in_progress");
        Assert.Equal(0, await CountAuthorizationsAsync(cardId));
        Assert.Empty(await ReadOutboxAsync(cardId));
    }

    [Fact]
    public async Task Event_of_a_failed_save_is_discarded_and_never_written_by_a_later_save_in_the_same_scope()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        (Guid cardId, _) = await GetCardOfAsync(purchase.Id);
        Guid eventId = Guid.NewGuid();

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
            Card card = await dbContext.Cards.SingleAsync(card => card.Id == cardId);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            // A second authorization with an existing id: the save fails on the primary key.
            scope.ServiceProvider.GetRequiredService<IAuthorizationRepository>().Add(CardAuthorization.Approve(
                purchase.Id, card, purchase.MerchantAccountId, Money.Create(30m, "BRL").Value, now));
            scope.ServiceProvider.GetRequiredService<IOutbox>().Add(
                new AuthorizationVoided(purchase.Id, cardId, purchase.MerchantAccountId, 30m, "BRL", now, now),
                eventId,
                now);
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await Assert.ThrowsAsync<IdempotencyClaimLostException>(() => unitOfWork.SaveChangesAsync());
            await unitOfWork.SaveChangesAsync();
        }

        OutboxEvent outboxEvent = Assert.Single(await ReadOutboxAsync(cardId));
        Assert.IsType<AuthorizationApproved>(outboxEvent.Event);
        Assert.NotEqual(eventId, outboxEvent.Message.EventId);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("void")]
    public async Task Capture_and_void_write_the_matching_event_with_the_status_change(string operation)
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        (Guid cardId, DateTimeOffset createdAt) = await GetCardOfAsync(purchase.Id);

        HttpResponseMessage response = await CompleteAsync(purchase.Id, operation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DateTimeOffset completedAt = (await ReadJsonAsync(response)).GetProperty("completedAt").GetDateTimeOffset();
        IReadOnlyList<OutboxEvent> events = await ReadOutboxAsync(cardId);
        Assert.Equal(2, events.Count);
        Assert.IsType<AuthorizationApproved>(events[0].Event);
        if (operation == "capture")
        {
            Assert.Equal(
                new AuthorizationCaptured(purchase.Id, cardId, purchase.MerchantAccountId, 30m, "BRL", createdAt, completedAt),
                events[1].Event);
            AssertRow(events[1], EventTypes.AuthorizationCaptured, cardId, occurredAt: completedAt);
        }
        else
        {
            Assert.Equal(
                new AuthorizationVoided(purchase.Id, cardId, purchase.MerchantAccountId, 30m, "BRL", createdAt, completedAt),
                events[1].Event);
            AssertRow(events[1], EventTypes.AuthorizationVoided, cardId, occurredAt: completedAt);
        }
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("void")]
    public async Task Capture_or_void_refused_or_failed_writes_no_event(string operation)
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        (Guid cardId, _) = await GetCardOfAsync(purchase.Id);
        using WebApplicationFactory<Program> ledgerDown = factory.WithLedgerDown();

        await AssertProblemAsync(
            await CompleteAsync(purchase.Id, operation, ledgerDown.CreateClient()),
            HttpStatusCode.ServiceUnavailable,
            "ledger_unavailable");
        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(purchase.Id, operation)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CompleteAsync(purchase.Id, operation)).StatusCode);

        // Only the approval and the one completion that changed the status.
        Assert.Equal(2, (await ReadOutboxAsync(cardId)).Count);
    }

    [Fact]
    public async Task Capture_whose_reply_was_lost_writes_the_captured_event_once_when_reconciled()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        (Guid cardId, _) = await GetCardOfAsync(purchase.Id);
        using WebApplicationFactory<Program> replyLost = factory.WithLedgerReplyLost(
            static request => request.RequestUri?.AbsolutePath.EndsWith("/capture", StringComparison.Ordinal) == true);

        HttpResponseMessage lost = await CompleteAsync(purchase.Id, "capture", replyLost.CreateClient());

        await AssertProblemAsync(lost, HttpStatusCode.ServiceUnavailable, "ledger_unavailable");
        Assert.Single(await ReadOutboxAsync(cardId));

        Assert.Equal(HttpStatusCode.OK, (await CompleteAsync(purchase.Id, "capture")).StatusCode);

        IReadOnlyList<OutboxEvent> events = await ReadOutboxAsync(cardId);
        Assert.Equal(2, events.Count);
        Assert.Equal(purchase.Id, Assert.IsType<AuthorizationCaptured>(events[1].Event).AuthorizationId);
    }

    [Fact]
    public async Task Capture_of_a_hold_voided_in_the_ledger_writes_an_authorization_voided_event()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        (Guid cardId, _) = await GetCardOfAsync(purchase.Id);
        Assert.Equal(HttpStatusCode.OK, (await _ledger.PostAsync($"/holds/{purchase.Id}/void", content: null)).StatusCode);

        await AssertProblemAsync(
            await CompleteAsync(purchase.Id, "capture"),
            HttpStatusCode.UnprocessableEntity,
            "authorization_not_capturable");

        // The local status became voided, so that is the event written with it.
        IReadOnlyList<OutboxEvent> events = await ReadOutboxAsync(cardId);
        Assert.Equal(2, events.Count);
        Assert.Equal(purchase.Id, Assert.IsType<AuthorizationVoided>(events[1].Event).AuthorizationId);
        Assert.Equal(
            AuthorizationEvents.EventIdOf(purchase.Id, typeof(AuthorizationVoided)),
            events[1].Message.EventId);
    }

    [Fact]
    public async Task Void_of_a_hold_captured_in_the_ledger_writes_an_authorization_captured_event()
    {
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        (Guid cardId, _) = await GetCardOfAsync(purchase.Id);
        Assert.Equal(
            HttpStatusCode.OK,
            (await _ledger.PostAsJsonAsync(
                $"/holds/{purchase.Id}/capture",
                new { merchantAccountId = purchase.MerchantAccountId })).StatusCode);

        await AssertProblemAsync(
            await CompleteAsync(purchase.Id, "void"),
            HttpStatusCode.UnprocessableEntity,
            "authorization_not_voidable");

        IReadOnlyList<OutboxEvent> events = await ReadOutboxAsync(cardId);
        Assert.Equal(2, events.Count);
        Assert.IsType<AuthorizationCaptured>(events[1].Event);
    }

    [Fact]
    public async Task Concurrent_captures_write_captured_events_that_all_share_one_event_id()
    {
        const int requests = 8;
        Purchase purchase = await AuthorizeApprovedAsync(100m, 30m);
        (Guid cardId, _) = await GetCardOfAsync(purchase.Id);

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, requests).Select(_ => Task.Run(() => CompleteAsync(purchase.Id, "capture"))));

        Assert.Contains(responses, static response => response.StatusCode == HttpStatusCode.OK);
        OutboxEvent[] captured = [.. (await ReadOutboxAsync(cardId)).Where(static e => e.Event is AuthorizationCaptured)];
        Assert.NotEmpty(captured);
        Assert.All(
            captured,
            e => Assert.Equal(AuthorizationEvents.EventIdOf(purchase.Id, typeof(AuthorizationCaptured)), e.Message.EventId));
        Assert.Equal(30m, await GetLedgerBalanceAsync(purchase.MerchantAccountId));
    }

    private static void AssertRow(OutboxEvent outboxEvent, string type, Guid cardId, DateTimeOffset occurredAt)
    {
        OutboxMessage message = outboxEvent.Message;
        Assert.Equal(type, message.Type);
        Assert.Equal(Topics.Authorizations, message.Topic);
        Assert.Equal(cardId.ToString("D", CultureInfo.InvariantCulture), message.PartitionKey);
        Assert.Null(message.PublishedAt);
        Assert.Equal(0, message.Attempts);
        Assert.NotEqual(default, message.CreatedAt);

        EventEnvelope envelope = outboxEvent.Envelope;
        Assert.Equal(message.EventId, envelope.EventId);
        Assert.Equal(type, envelope.Type);
        Assert.Equal(1, envelope.Version);
        Assert.Equal(message.PartitionKey, envelope.PartitionKey);
        Assert.Equal(occurredAt, envelope.OccurredAt);
    }

    /// <summary>The outbox rows of <paramref name="cardId"/> (their partition key), in the order they were written.</summary>
    private async Task<IReadOnlyList<OutboxEvent>> ReadOutboxAsync(Guid cardId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        string partitionKey = cardId.ToString("D", CultureInfo.InvariantCulture);
        List<OutboxMessage> messages = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.PartitionKey == partitionKey)
            .OrderBy(message => message.Id)
            .ToListAsync();

        return [.. messages.Select(static message =>
        {
            EventEnvelope envelope = message.ReadEnvelope();
            return new OutboxEvent(message, envelope, EventSerializer.DeserializePayload(envelope));
        })];
    }

    private async Task<(Guid CardId, DateTimeOffset CreatedAt)> GetCardOfAsync(Guid authorizationId)
    {
        JsonElement authorization = await ReadJsonAsync(await _client.GetAsync($"/authorizations/{authorizationId}"));
        return (authorization.GetProperty("cardId").GetGuid(), authorization.GetProperty("createdAt").GetDateTimeOffset());
    }

    private async Task InsertApprovedAuthorizationAsync(Guid id, Guid cardId, Guid merchantAccountId)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        Card card = await dbContext.Cards.SingleAsync(card => card.Id == cardId);
        dbContext.Authorizations.Add(CardAuthorization.Approve(
            id, card, merchantAccountId, Money.Create(30m, "BRL").Value, DateTimeOffset.UtcNow));
        await dbContext.SaveChangesAsync();
    }

    private async Task TakeIdempotencyClaimOverAsync(string key)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        AuthorizationDbContext dbContext = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        await dbContext.IdempotencyKeys
            .Where(record => record.Key == key)
            .ExecuteUpdateAsync(setters => setters.SetProperty(record => record.ClaimToken, (Guid?)Guid.NewGuid()));
    }

    private sealed record OutboxEvent(OutboxMessage Message, EventEnvelope Envelope, IIntegrationEvent Event);
}
