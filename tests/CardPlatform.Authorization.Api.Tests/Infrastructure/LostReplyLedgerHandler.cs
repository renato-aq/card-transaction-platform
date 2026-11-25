namespace CardPlatform.Authorization.Api.Tests.Infrastructure;

/// <summary>
/// Primary handler of a Ledger whose replies to <c>POST /holds</c> are lost: the request reaches the Ledger (so the
/// hold is placed), but the connection drops before the reply arrives. Other requests pass through.
/// </summary>
internal sealed class LostReplyLedgerHandler(HttpMessageHandler ledger) : DelegatingHandler(ledger)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/holds")
        {
            response.Dispose();
            throw new HttpRequestException(
                HttpRequestError.ResponseEnded,
                "The connection was closed before the Ledger's reply arrived (ledger.test).");
        }

        return response;
    }
}
