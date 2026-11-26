using CardPlatform.Fraud.Domain.Rules;

namespace CardPlatform.Fraud.Infrastructure.Rules;

/// <summary>
/// Limits of the fraud rules (configuration section <c>Fraud:Rules</c>), validated on startup and handed to the domain
/// as a <see cref="FraudRules"/>. The defaults are the initial limits: 5 approved purchases within 60 s; an amount above
/// 3× the average of at least 5 previous purchases in the same currency.
/// </summary>
public sealed class FraudRulesOptions
{
    public const string SectionName = "Fraud:Rules";

    /// <summary><c>Fraud:Rules:Velocity</c>.</summary>
    public VelocityRuleOptions Velocity { get; set; } = new();

    /// <summary><c>Fraud:Rules:AmountAboveAverage</c>.</summary>
    public AmountAboveAverageRuleOptions AmountAboveAverage { get; set; } = new();

    /// <summary>The rules with these limits.</summary>
    public FraudRules ToRules()
    {
        return new FraudRules(
            new VelocityRule(Velocity.Threshold, Velocity.Window),
            new AmountAboveAverageRule(AmountAboveAverage.Multiplier, AmountAboveAverage.MinimumHistory));
    }

    internal bool IsValid()
    {
        return Velocity is not null
            && AmountAboveAverage is not null
            && Velocity.Threshold >= VelocityRule.MinimumThreshold
            && Velocity.Window > TimeSpan.Zero
            && AmountAboveAverage.Multiplier > 1m
            && AmountAboveAverage.MinimumHistory >= 1;
    }
}

/// <summary>Limits of the <see cref="VelocityRule"/>.</summary>
public sealed class VelocityRuleOptions
{
    /// <summary>How many approved purchases of a card within <see cref="Window"/> fire the rule; at least 2. Default 5.</summary>
    public int Threshold { get; set; } = VelocityRule.DefaultThreshold;

    /// <summary>How far back from each purchase to count; positive. Default 60 s (<c>00:01:00</c>).</summary>
    public TimeSpan Window { get; set; } = VelocityRule.DefaultWindow;
}

/// <summary>Limits of the <see cref="AmountAboveAverageRule"/>.</summary>
public sealed class AmountAboveAverageRuleOptions
{
    /// <summary>How many times the card's average an amount must exceed; greater than 1. Default 3.</summary>
    public decimal Multiplier { get; set; } = AmountAboveAverageRule.DefaultMultiplier;

    /// <summary>How many previous approved purchases in the currency the rule needs; at least 1. Default 5.</summary>
    public int MinimumHistory { get; set; } = AmountAboveAverageRule.DefaultMinimumHistory;
}
