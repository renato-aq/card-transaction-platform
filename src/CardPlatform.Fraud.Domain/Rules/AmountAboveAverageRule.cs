using System.Globalization;

using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Domain.Rules;

/// <summary>
/// An amount far above what the card usually spends: fires when the purchase's amount is greater than
/// <see cref="Multiplier"/> times the average of the card's previous approved purchases in the same currency, and only
/// once there are at least <see cref="MinimumHistory"/> of them (with fewer, the average says little). Amounts in other
/// currencies are never mixed in.
/// </summary>
public sealed class AmountAboveAverageRule
{
    /// <summary>The rule's code in <see cref="RuleViolation.Rule"/>; part of the published contract, never changes.</summary>
    public const string Code = "amount_above_average";

    public const decimal DefaultMultiplier = 3m;

    public const int DefaultMinimumHistory = 5;

    /// <param name="multiplier">How many times the average an amount must exceed; greater than one.</param>
    /// <param name="minimumHistory">How many previous purchases in the currency the rule needs; at least one.</param>
    public AmountAboveAverageRule(decimal multiplier, int minimumHistory)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(multiplier, 1m);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumHistory, 1);

        Multiplier = multiplier;
        MinimumHistory = minimumHistory;
    }

    public decimal Multiplier { get; }

    public int MinimumHistory { get; }

    /// <summary>The violation, if <paramref name="purchase"/> fires the rule given its card's <paramref name="activity"/>.</summary>
    public RuleViolation? Evaluate(CardPurchase purchase, CardActivity activity)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        ArgumentNullException.ThrowIfNull(activity);

        SpendingSummary previous = activity.PreviousSpending;
        if (previous.Count < MinimumHistory)
        {
            return null;
        }

        // amount > multiplier × (total / count), compared without dividing so no rounding decides the outcome.
        if (purchase.Amount.Amount * previous.Count <= Multiplier * previous.Total)
        {
            return null;
        }

        string currency = purchase.Amount.Currency;
        decimal average = decimal.Round(previous.Total / previous.Count, 2, MidpointRounding.AwayFromZero);

        return new RuleViolation(
            Code,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Amount {purchase.Amount} is above {Multiplier:0.##}x the average of {average:0.00} {currency} over {previous.Count} previous approved purchases in {currency}."));
    }
}
