using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Domain.Rules;

/// <summary>
/// What the fraud rules need to know about a card's past to judge one of its purchases, relative to that purchase's own
/// <see cref="CardPurchase.AuthorizedAt"/> (not the time it is processed, so a replay judges it the same way). Only
/// approved purchases count (including those captured or voided since), and never the judged purchase itself:
/// <list type="bullet">
/// <item><see cref="RecentApprovedCount"/>: those authorized in the window <c>(t − window, t]</c>;</item>
/// <item><see cref="PreviousSpending"/>: those authorized at or before <c>t</c> in the purchase's currency.</item>
/// </list>
/// <see cref="Of"/> computes it from a whole <see cref="CardHistory"/>; a repository may compute the same counts with
/// bounded queries instead of loading every purchase of the card.
/// </summary>
/// <param name="RecentApprovedCount">Approved purchases of the card in the velocity window before the purchase.</param>
/// <param name="PreviousSpending">Approved purchases of the card in the purchase's currency up to the purchase.</param>
public sealed record CardActivity(int RecentApprovedCount, SpendingSummary PreviousSpending)
{
    public int RecentApprovedCount { get; } = RecentApprovedCount >= 0
        ? RecentApprovedCount
        : throw new ArgumentOutOfRangeException(nameof(RecentApprovedCount), RecentApprovedCount, "The count of purchases cannot be negative.");

    public SpendingSummary PreviousSpending { get; } = PreviousSpending ?? throw new ArgumentNullException(nameof(PreviousSpending));

    /// <summary>The activity of <paramref name="purchase"/>'s card before it, read from the card's whole history.</summary>
    /// <param name="purchase">The judged purchase; ignored if <paramref name="history"/> already contains it.</param>
    /// <param name="history">The card's purchases.</param>
    /// <param name="window">The length of the velocity window, ending at the purchase.</param>
    public static CardActivity Of(CardPurchase purchase, CardHistory history, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        if (history.CardId != purchase.CardId)
        {
            throw new ArgumentException(
                $"The history of card {history.CardId} cannot judge purchase {purchase.Id} of card {purchase.CardId}.",
                nameof(history));
        }

        DateTimeOffset at = purchase.AuthorizedAt;
        DateTimeOffset windowStart = at - window;

        CardPurchase[] previous =
        [
            .. history.Approved.Where(other => other.Id != purchase.Id && other.AuthorizedAt <= at),
        ];

        int recent = previous.Count(other => other.AuthorizedAt > windowStart);

        CardPurchase[] sameCurrency =
        [
            .. previous.Where(other => string.Equals(other.Amount.Currency, purchase.Amount.Currency, StringComparison.Ordinal)),
        ];

        return new CardActivity(
            recent,
            new SpendingSummary(sameCurrency.Length, sameCurrency.Sum(static other => other.Amount.Amount)));
    }
}
