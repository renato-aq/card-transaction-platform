using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CardPlatform.Authorization.Infrastructure.Tests.Ledger;

/// <summary>A request the fake Ledger received, with its body read as text.</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body)
{
    public JsonElement Json => JsonDocument.Parse(Body ?? "null").RootElement;
}

/// <summary>
/// Fake Ledger used as the primary handler of the Ledger <see cref="HttpClient"/>, below the resilience and service
/// discovery handlers: it records every attempt and answers with the scripted <see cref="Respond"/> function, which
/// receives the zero-based attempt number.
/// </summary>
internal sealed class FakeLedgerHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    public Func<RecordedRequest, int, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        static (_, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotImplemented));

    public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

    public static HttpResponseMessage Json(HttpStatusCode status, object body)
    {
        return new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
    }

    public static HttpResponseMessage Problem(HttpStatusCode status, string code, string detail)
    {
        HttpResponseMessage response = new(status)
        {
            Content = JsonContent.Create(new { type = "about:blank", title = "Error", status = (int)status, detail, code }),
        };
        response.Content.Headers.ContentType = new("application/problem+json");
        return response;
    }

    public static async Task<HttpResponseMessage> HangAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        RecordedRequest recorded = new(request.Method, request.RequestUri!, body);
        int attempt;
        lock (_requests)
        {
            attempt = _requests.Count;
            _requests.Enqueue(recorded);
        }

        HttpResponseMessage response = await Respond(recorded, attempt, cancellationToken);
        response.RequestMessage = request;
        return response;
    }
}
