namespace CardPlatform.Authorization.Api.Tests.Infrastructure;

/// <summary>
/// Primary handler of a Ledger that runs <paramref name="beforeSend"/> before each request reaches it, e.g. to change
/// the Authorization's database while a request is between its idempotency claim and its save.
/// </summary>
internal sealed class InterceptingLedgerHandler(HttpMessageHandler ledger, Func<HttpRequestMessage, Task> beforeSend)
    : DelegatingHandler(ledger)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await beforeSend(request);
        return await base.SendAsync(request, cancellationToken);
    }
}
