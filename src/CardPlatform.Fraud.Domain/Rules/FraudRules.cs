using CardPlatform.Fraud.Domain.Purchases;

namespace CardPlatform.Fraud.Domain.Rules;

/// <summary>
/// The fraud rules every approved purchase is judged by: <see cref="Velocity"/> and <see cref="AmountAboveAverage"/>.
/// A purchase that fires any of them raises one <see cref="FraudSuspicion"/> naming all those that fired.
/// </summary>
public sealed class FraudRules
{
    public FraudRules(VelocityRule velocity, AmountAboveAverageRule amountAboveAverage)
    {
        ArgumentNullException.ThrowIfNull(velocity);
        ArgumentNullException.ThrowIfNull(amountAboveAverage);

        Velocity = velocity;
        AmountAboveAverage = amountAboveAverage;
    }

    /// <summary>
    /// The initial limits: 5 approved purchases within 60 s; an amount above 3× the average of at least 5 previous
    /// purchases.
    /// </summary>
    public static FraudRules Default { get; } = new(
        new VelocityRule(VelocityRule.DefaultThreshold, VelocityRule.DefaultWindow),
        new AmountAboveAverageRule(AmountAboveAverageRule.DefaultMultiplier, AmountAboveAverageRule.DefaultMinimumHistory));

    public VelocityRule Velocity { get; }

    public AmountAboveAverageRule AmountAboveAverage { get; }

    /// <summary>
    /// The suspicion <paramref name="purchase"/> raises given its card's <paramref name="activity"/> before it
    /// (<see cref="CardActivity"/>, with the window of <see cref="Velocity"/>), or <see langword="null"/>. A declined
    /// purchase is not judged.
    /// </summary>
    public FraudSuspicion? Evaluate(CardPurchase purchase, CardActivity activity)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        ArgumentNullException.ThrowIfNull(activity);

        if (!purchase.WasApproved)
        {
            return null;
        }

        RuleViolation[] violations =
        [
            .. new[] { Velocity.Evaluate(purchase, activity), AmountAboveAverage.Evaluate(purchase, activity) }.OfType<RuleViolation>(),
        ];

        return violations.Length == 0 ? null : new FraudSuspicion(purchase.CardId, purchase.Id, violations);
    }

    /// <summary>
    /// The suspicion <paramref name="purchase"/> raises given its card's whole <paramref name="history"/>, or
    /// <see langword="null"/>.
    /// </summary>
    public FraudSuspicion? Evaluate(CardPurchase purchase, CardHistory history)
    {
        return Evaluate(purchase, CardActivity.Of(purchase, history, Velocity.Window));
    }
}
