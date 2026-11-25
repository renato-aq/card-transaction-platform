using Microsoft.Extensions.Http.Resilience;

using Polly;
using Polly.Retry;

namespace CardPlatform.Authorization.Infrastructure.Ledger;

/// <summary>
/// Decides which Ledger requests the resilience pipeline may retry: reads (<c>GET</c>) and requests the client marks
/// as idempotent (placing a hold, idempotent by id). Opening an account, capturing and voiding are never retried,
/// because repeating them is not safe (a new account, or <c>hold_not_pending</c>).
/// </summary>
internal static class LedgerRequestPolicy
{
    public static readonly HttpRequestOptionsKey<bool> Idempotent = new("CardPlatform.Ledger.Idempotent");

    public static void MarkIdempotent(HttpRequestMessage request)
    {
        request.Options.Set(Idempotent, true);
    }

    public static bool IsSafeToRetry(HttpRequestMessage? request)
    {
        if (request is null)
        {
            return false;
        }

        return request.Method == HttpMethod.Get
            || (request.Options.TryGetValue(Idempotent, out bool idempotent) && idempotent);
    }

    /// <summary>Applies the Ledger settings to the standard resilience pipeline of the client.</summary>
    public static void Configure(HttpStandardResilienceOptions resilience, LedgerClientOptions options)
    {
        resilience.AttemptTimeout.Timeout = options.AttemptTimeout;
        resilience.TotalRequestTimeout.Timeout = options.TotalRequestTimeout;

        resilience.Retry.MaxRetryAttempts = options.MaxRetryAttempts;
        resilience.Retry.Delay = options.RetryBaseDelay;
        resilience.Retry.BackoffType = DelayBackoffType.Exponential;
        resilience.Retry.UseJitter = true;

        // Keep the default transient-failure predicate (network errors, timeouts, 408, 429, 5xx), but only for safe requests.
        Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>> isTransient = resilience.Retry.ShouldHandle;
        resilience.Retry.ShouldHandle = args =>
            IsSafeToRetry(args.Outcome.Result?.RequestMessage ?? args.Context.GetRequestMessage())
                ? isTransient(args)
                : ValueTask.FromResult(false);

        // The circuit breaker keeps its defaults; its sampling duration (30 s) must be at least twice the attempt timeout.
    }
}
