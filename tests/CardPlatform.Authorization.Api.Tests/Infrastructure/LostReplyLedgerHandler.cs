namespace CardPlatform.Authorization.Api.Tests.Infrastructure;

/// <summary>
/// Primary handler of a Ledger whose replies to some requests are lost: the request reaches the Ledger (so it takes
/// effect, e.g. the hold is placed or captured), but the connection drops before the reply arrives. The lost requests
/// are those matching <paramref name="losesReply"/> (by default <c>POST /holds</c>); other requests pass through.
/// </summary>
internal sealed class LostReplyLedgerHandler(HttpMessageHandler ledger, Func<HttpRequestMessage, bool>? losesReply = null)
    : DelegatingHandler(ledger)
{
    private readonly Func<HttpRequestMessage, bool> _losesReply = losesReply ?? IsPlaceHold;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        if (_losesReply(request))
        {
            response.Dispose();
            throw new HttpRequestException(
                HttpRequestError.ResponseEnded,
                "The connection was closed before the Ledger's reply arrived (ledger.test).");
        }

        return response;
    }

    private static bool IsPlaceHold(HttpRequestMessage request)
    {
        return request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/holds";
    }
}
