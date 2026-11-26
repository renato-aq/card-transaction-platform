using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Domain.Tests.Purchases;

internal static class PurchaseSamples
{
    public static readonly DateTimeOffset AuthorizedAt = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    public static Money Brl(decimal amount)
    {
        return Money.Create(amount, "BRL").Value;
    }

    public static CardPurchase Approved(Guid cardId, decimal amount = 30m, DateTimeOffset? authorizedAt = null, string currency = "BRL")
    {
        return CardPurchase.Approve(
            Guid.CreateVersion7(), cardId, Guid.NewGuid(), Money.Create(amount, currency).Value, authorizedAt ?? AuthorizedAt).Value;
    }

    public static CardPurchase Declined(Guid cardId, decimal amount = 30m, DateTimeOffset? authorizedAt = null)
    {
        return CardPurchase.Decline(
            Guid.CreateVersion7(), cardId, Guid.NewGuid(), Brl(amount), "insufficient_funds", authorizedAt ?? AuthorizedAt).Value;
    }
}
