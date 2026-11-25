using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Domain.Common;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace CardPlatform.Authorization.Infrastructure.Tests.Ledger;

/// <summary>
/// Retries, timeouts and the circuit breaker of the Ledger client, exercised through the real resilience pipeline
/// (only the primary handler is fake).
/// </summary>
public sealed class LedgerClientResilienceTests : IDisposable
{
    // Room for scheduling jitter on a busy CI machine, on top of the configured total timeout.
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(750);

    private readonly LedgerClientHarness _ledger = new();

    public void Dispose()
    {
        _ledger.Dispose();
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("hold")]
    [InlineData("place")]
    public async Task Repeated_5xx_on_a_safe_call_is_retried_and_becomes_ledger_unavailable_within_the_timeout(string operation)
    {
        _ledger.Handler.Respond = static (_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Stopwatch elapsed = Stopwatch.StartNew();
        Result result = await LedgerHttpClientTests.CallAsync(_ledger.Client, operation);
        elapsed.Stop();

        AssertLedgerUnavailable(result);
        Assert.Equal(1 + LedgerClientHarness.MaxRetryAttempts, _ledger.Handler.Requests.Count);
        Assert.True(
            elapsed.Elapsed < LedgerClientHarness.TotalRequestTimeout + Tolerance,
            $"Took {elapsed.Elapsed}, total timeout is {LedgerClientHarness.TotalRequestTimeout}.");
    }

    [Fact]
    public async Task Ledger_that_never_answers_becomes_ledger_unavailable_within_the_total_timeout()
    {
        _ledger.Handler.Respond = static (_, _, cancellationToken) => FakeLedgerHandler.HangAsync(cancellationToken);

        Stopwatch elapsed = Stopwatch.StartNew();
        Result<LedgerBalance> result = await _ledger.Client.GetBalanceAsync(Guid.NewGuid());
        elapsed.Stop();

        AssertLedgerUnavailable(result);
        Assert.True(_ledger.Handler.Requests.Count > 1, "A timed-out read should be retried.");
        Assert.True(
            elapsed.Elapsed < LedgerClientHarness.TotalRequestTimeout + Tolerance,
            $"Took {elapsed.Elapsed}, total timeout is {LedgerClientHarness.TotalRequestTimeout}.");
    }

    [Fact]
    public async Task Connection_failure_becomes_ledger_unavailable()
    {
        _ledger.Handler.Respond = static (_, _, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused."));

        Result<LedgerBalance> result = await _ledger.Client.GetBalanceAsync(Guid.NewGuid());

        AssertLedgerUnavailable(result);
        Assert.Equal(1 + LedgerClientHarness.MaxRetryAttempts, _ledger.Handler.Requests.Count);
    }

    [Fact]
    public async Task Place_hold_retried_after_a_timeout_reuses_the_hold_id_and_creates_a_single_hold()
    {
        // Emulates the Ledger (spec 001): the hold is stored on the first attempt, whose response is lost to the
        // attempt timeout; the retry with the same id returns the existing hold (200) instead of creating another.
        ConcurrentDictionary<Guid, object> holds = new();
        _ledger.Handler.Respond = async (request, attempt, cancellationToken) =>
        {
            Guid id = request.Json.GetProperty("id").GetGuid();
            object hold = new
            {
                id,
                cardAccountId = request.Json.GetProperty("cardAccountId").GetGuid(),
                amount = request.Json.GetProperty("amount").GetDecimal(),
                currency = request.Json.GetProperty("currency").GetString(),
                status = "pending",
                createdAt = DateTimeOffset.UtcNow,
                completedAt = (DateTimeOffset?)null,
            };
            bool created = holds.TryAdd(id, hold);

            if (attempt == 0)
            {
                return await FakeLedgerHandler.HangAsync(cancellationToken);
            }

            return FakeLedgerHandler.Json(created ? HttpStatusCode.Created : HttpStatusCode.OK, holds[id]);
        };

        Guid holdId = Guid.NewGuid();
        Result<LedgerHold> result = await _ledger.Client.PlaceHoldAsync(holdId, Guid.NewGuid(), 30.00m, "BRL");

        Assert.True(result.IsSuccess);
        Assert.Equal(holdId, result.Value.Id);
        Assert.Equal(2, _ledger.Handler.Requests.Count);
        Assert.All(_ledger.Handler.Requests, request => Assert.Equal(holdId, request.Json.GetProperty("id").GetGuid()));
        Assert.Equal(holdId, Assert.Single(holds).Key);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("capture")]
    [InlineData("void")]
    public async Task Unsafe_call_is_not_retried_on_5xx(string operation)
    {
        _ledger.Handler.Respond = static (_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Result result = await LedgerHttpClientTests.CallAsync(_ledger.Client, operation);

        AssertLedgerUnavailable(result);
        Assert.Single(_ledger.Handler.Requests);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("capture")]
    [InlineData("void")]
    public async Task Unsafe_call_is_not_retried_on_timeout(string operation)
    {
        _ledger.Handler.Respond = static (_, _, cancellationToken) => FakeLedgerHandler.HangAsync(cancellationToken);

        Stopwatch elapsed = Stopwatch.StartNew();
        Result result = await LedgerHttpClientTests.CallAsync(_ledger.Client, operation);
        elapsed.Stop();

        AssertLedgerUnavailable(result);
        Assert.Single(_ledger.Handler.Requests);
        Assert.True(
            elapsed.Elapsed < LedgerClientHarness.AttemptTimeout + Tolerance,
            $"Took {elapsed.Elapsed}, attempt timeout is {LedgerClientHarness.AttemptTimeout}.");
    }

    [Fact]
    public async Task Circuit_opens_after_repeated_failures_and_short_circuits_calls_to_the_ledger()
    {
        // Standard circuit breaker: opens when at least 10% of at least 100 attempts in 30 s fail.
        _ledger.Handler.Respond = static (_, _, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        for (int call = 0; call < 40; call++)
        {
            AssertLedgerUnavailable(await _ledger.Client.GetBalanceAsync(Guid.NewGuid()));
        }

        int requestsBefore = _ledger.Handler.Requests.Count;
        Result<LedgerBalance> result = await _ledger.Client.GetBalanceAsync(Guid.NewGuid());

        AssertLedgerUnavailable(result);
        Assert.Equal(requestsBefore, _ledger.Handler.Requests.Count);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_reported_as_ledger_unavailable()
    {
        _ledger.Handler.Respond = static (_, _, cancellationToken) => FakeLedgerHandler.HangAsync(cancellationToken);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _ledger.Client.GetBalanceAsync(Guid.NewGuid(), cancellation.Token));
    }

    [Fact]
    public void Ledger_client_has_a_single_resilience_handler_outside_service_discovery()
    {
        // The generic handler of ServiceDefaults is replaced (not stacked) and, as in ServiceDefaults, resilience wraps
        // service discovery, so each retry resolves the endpoint again.
        HttpMessageHandler? handler = _ledger.Services
            .GetRequiredService<IHttpMessageHandlerFactory>()
            .CreateHandler(DependencyInjection.LedgerHttpClientName);

        List<string> chain = [];
        while (handler is DelegatingHandler delegating)
        {
            chain.Add(delegating is ResilienceHandler ? "resilience" : delegating.GetType().Name);
            handler = delegating.InnerHandler;
        }

        Assert.Single(chain, static name => name == "resilience");
        int resilience = chain.IndexOf("resilience");
        int serviceDiscovery = chain.FindIndex(static name => name.Contains("Resolving", StringComparison.Ordinal));
        Assert.True(serviceDiscovery > resilience, $"Handler chain: {string.Join(" -> ", chain)}.");
    }

    private static void AssertLedgerUnavailable(Result result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(LedgerErrors.UnavailableCode, result.Error.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }
}
