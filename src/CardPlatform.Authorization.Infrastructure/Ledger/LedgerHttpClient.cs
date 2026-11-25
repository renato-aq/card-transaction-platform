using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Domain.Common;

using Microsoft.Extensions.Logging;

using Polly.CircuitBreaker;
using Polly.Timeout;

namespace CardPlatform.Authorization.Infrastructure.Ledger;

/// <summary>
/// HTTP adapter of <see cref="ILedgerClient"/> (typed <see cref="HttpClient"/>). Retries, timeouts and the circuit
/// breaker come from the standard resilience pipeline configured in <see cref="LedgerRequestPolicy"/>; this class
/// builds the requests, marks the idempotent ones and translates responses into <see cref="Result"/>s.
/// </summary>
internal sealed partial class LedgerHttpClient(HttpClient httpClient, ILogger<LedgerHttpClient> logger) : ILedgerClient
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public Task<Result<LedgerAccount>> OpenCardAccountAsync(string currency, CancellationToken cancellationToken = default)
    {
        return SendAsync<AccountResponse, LedgerAccount>(
            () => Post("accounts", new OpenAccountRequest("card", currency)),
            static account => new LedgerAccount(account.Id, account.Currency, account.Balance.Amount),
            cancellationToken);
    }

    public Task<Result<LedgerBalance>> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return SendAsync<AccountBalanceResponse, LedgerBalance>(
            () => new HttpRequestMessage(HttpMethod.Get, $"accounts/{accountId}/balance"),
            static balance => new LedgerBalance(balance.AccountId, balance.Balance.Amount, balance.Balance.Currency),
            cancellationToken);
    }

    public Task<Result<LedgerHold>> PlaceHoldAsync(
        Guid holdId,
        Guid cardAccountId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<HoldResponse, LedgerHold>(
            () =>
            {
                HttpRequestMessage request = Post("holds", new PlaceHoldRequest(holdId, cardAccountId, amount, currency));

                // The Ledger is idempotent by hold id (spec 001): a retry returns the same hold instead of a second one.
                LedgerRequestPolicy.MarkIdempotent(request);
                return request;
            },
            ToHold,
            cancellationToken);
    }

    public Task<Result<LedgerHold>> CaptureHoldAsync(
        Guid holdId,
        Guid merchantAccountId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<HoldResponse, LedgerHold>(
            () => Post($"holds/{holdId}/capture", new CaptureHoldRequest(merchantAccountId)),
            ToHold,
            cancellationToken);
    }

    public Task<Result<LedgerHold>> VoidHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        return SendAsync<HoldResponse, LedgerHold>(
            () => new HttpRequestMessage(HttpMethod.Post, $"holds/{holdId}/void"),
            ToHold,
            cancellationToken);
    }

    public Task<Result<LedgerHold>> GetHoldAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        return SendAsync<HoldResponse, LedgerHold>(
            () => new HttpRequestMessage(HttpMethod.Get, $"holds/{holdId}"),
            ToHold,
            cancellationToken);
    }

    private static HttpRequestMessage Post<TBody>(string path, TBody body)
    {
        return new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: JsonOptions) };
    }

    private static LedgerHold ToHold(HoldResponse hold)
    {
        return new LedgerHold(
            hold.Id,
            hold.CardAccountId,
            hold.Amount,
            hold.Currency,
            hold.Status,
            hold.CreatedAt,
            hold.CompletedAt);
    }

    private async Task<Result<TResult>> SendAsync<TResponse, TResult>(
        Func<HttpRequestMessage> createRequest,
        Func<TResponse, TResult> map,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = createRequest();

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCommunicationFailure(exception, cancellationToken))
        {
            LogLedgerUnavailable(logger, exception, request.Method, request.RequestUri);
            return Result.Failure<TResult>(LedgerErrors.Unavailable());
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                TResponse body = await response.Content
                    .ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"The Ledger answered {(int)response.StatusCode} to {request.Method} {request.RequestUri} without a body.");
                return Result.Success(map(body));
            }

            if (IsUnavailableStatus(response.StatusCode))
            {
                LogLedgerFailedStatus(logger, request.Method, request.RequestUri, (int)response.StatusCode);
                return Result.Failure<TResult>(LedgerErrors.Unavailable());
            }

            Error error = await TranslateErrorAsync(request, response, cancellationToken).ConfigureAwait(false);
            return Result.Failure<TResult>(error);
        }
    }

    /// <summary>
    /// Translates a Ledger 4xx problem into an Authorization error, keeping the Ledger code. Any other status, or a
    /// problem without a code, means the two services disagree on the contract: that is a bug and throws.
    /// </summary>
    private static async Task<Error> TranslateErrorAsync(
        HttpRequestMessage request,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        LedgerProblem? problem = null;
        try
        {
            problem = await response.Content
                .ReadFromJsonAsync<LedgerProblem>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Not a problem document: handled below as an unexpected response.
        }

        ErrorType? type = response.StatusCode switch
        {
            HttpStatusCode.BadRequest => ErrorType.Validation,
            HttpStatusCode.NotFound => ErrorType.NotFound,
            HttpStatusCode.Conflict => ErrorType.Conflict,
            HttpStatusCode.UnprocessableEntity => ErrorType.BusinessRule,
            _ => null,
        };

        if (type is null || string.IsNullOrWhiteSpace(problem?.Code))
        {
            throw new InvalidOperationException(
                $"Unexpected Ledger response {(int)response.StatusCode} to {request.Method} {request.RequestUri}" +
                $" (code: {problem?.Code ?? "none"}).");
        }

        string message = problem.Detail ?? problem.Title ?? problem.Code;
        return new Error(problem.Code, message, type.Value);
    }

    private static bool IsUnavailableStatus(HttpStatusCode statusCode)
    {
        return (int)statusCode >= 500
            || statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
    }

    private static bool IsCommunicationFailure(Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            HttpRequestException or TimeoutRejectedException or BrokenCircuitException => true,

            // A cancellation that the caller did not ask for is a timeout (e.g. HttpClient.Timeout).
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false,
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);

        // The Ledger serializes enums (e.g. hold status) as camelCase strings.
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Ledger could not be reached for {Method} {Uri}.")]
    private static partial void LogLedgerUnavailable(ILogger logger, Exception exception, HttpMethod method, Uri? uri);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Ledger answered {StatusCode} to {Method} {Uri}.")]
    private static partial void LogLedgerFailedStatus(ILogger logger, HttpMethod method, Uri? uri, int statusCode);
}
