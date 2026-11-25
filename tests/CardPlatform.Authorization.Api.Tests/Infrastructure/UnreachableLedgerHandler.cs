using System.Net.Sockets;

namespace CardPlatform.Authorization.Api.Tests.Infrastructure;

/// <summary>Primary handler of a Ledger that is down: every request fails as a refused connection.</summary>
internal sealed class UnreachableLedgerHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new HttpRequestException(
            HttpRequestError.ConnectionError,
            "Connection refused (ledger.test).",
            new SocketException((int)SocketError.ConnectionRefused));
    }
}
