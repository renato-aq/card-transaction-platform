using System.Globalization;

using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Domain.Rules;

/// <summary>
/// Too many approved purchases of a card in a short time: fires when the purchase is the <see cref="Threshold"/>-th
/// (or later) approved purchase of its card authorized within <see cref="Window"/>, that is, when the approved
/// purchases authorized in <c>(t − window, t]</c>, the judged one included, are at least <see cref="Threshold"/>.
/// </summary>
public sealed class VelocityRule
{
    /// <summary>The rule's code in <see cref="RuleViolation.Rule"/>; part of the published contract, never changes.</summary>
    public const string Code = "velocity";

    public const int DefaultThreshold = 5;

    /// <summary>The smallest <see cref="Threshold"/>: with one, every purchase would fire.</summary>
    public const int MinimumThreshold = 2;

    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);

    /// <param name="threshold">How many approved purchases in the window fire the rule; at least <see cref="MinimumThreshold"/>.</param>
    /// <param name="window">How far back from the purchase to look; positive.</param>
    public VelocityRule(int threshold, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, MinimumThreshold);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        Threshold = threshold;
        Window = window;
    }

    public int Threshold { get; }

    public TimeSpan Window { get; }

    /// <summary>The violation, if <paramref name="purchase"/> fires the rule given its card's <paramref name="activity"/>.</summary>
    public RuleViolation? Evaluate(CardPurchase purchase, CardActivity activity)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        ArgumentNullException.ThrowIfNull(activity);

        int count = activity.RecentApprovedCount + 1;
        if (count < Threshold)
        {
            return null;
        }

        return new RuleViolation(
            Code,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{count} approved purchases of the card within {Window.TotalSeconds:0.###} s (threshold: {Threshold})."));
    }
}
