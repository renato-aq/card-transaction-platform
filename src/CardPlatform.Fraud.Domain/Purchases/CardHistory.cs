namespace CardPlatform.Fraud.Domain.Purchases;

/// <summary>
/// The purchases recorded for one card, oldest first: what the fraud rules evaluate a new purchase against (how many
/// approved purchases in a time window, the usual approved amount in a currency).
/// </summary>
public sealed class CardHistory
{
    public CardHistory(Guid cardId, IEnumerable<CardPurchase> purchases)
    {
        ArgumentNullException.ThrowIfNull(purchases);

        CardPurchase[] ordered = [.. purchases.OrderBy(static purchase => purchase.AuthorizedAt).ThenBy(static purchase => purchase.Id)];
        if (ordered.FirstOrDefault(purchase => purchase.CardId != cardId) is { } other)
        {
            throw new ArgumentException($"Purchase {other.Id} belongs to card {other.CardId}, not {cardId}.", nameof(purchases));
        }

        CardId = cardId;
        Purchases = ordered;
    }

    public Guid CardId { get; }

    /// <summary>Every recorded purchase, approved or declined, by <see cref="CardPurchase.AuthorizedAt"/>.</summary>
    public IReadOnlyList<CardPurchase> Purchases { get; }

    /// <summary>The purchases the Authorization approved (including those captured or voided since).</summary>
    public IEnumerable<CardPurchase> Approved => Purchases.Where(static purchase => purchase.WasApproved);

    /// <summary>The purchases the Authorization declined.</summary>
    public IEnumerable<CardPurchase> Declined => Purchases.Where(static purchase => !purchase.WasApproved);

    /// <summary>The approved purchases authorized at or after <paramref name="since"/>.</summary>
    public IEnumerable<CardPurchase> ApprovedSince(DateTimeOffset since)
    {
        return Approved.Where(purchase => purchase.AuthorizedAt >= since);
    }

    /// <summary>The approved purchases in <paramref name="currency"/>.</summary>
    public IEnumerable<CardPurchase> ApprovedIn(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        return Approved.Where(purchase => string.Equals(purchase.Amount.Currency, currency, StringComparison.Ordinal));
    }
}
