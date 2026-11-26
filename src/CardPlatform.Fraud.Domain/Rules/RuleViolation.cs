namespace CardPlatform.Fraud.Domain.Rules;

/// <summary>A fraud rule that fired for a purchase.</summary>
/// <param name="Rule">The rule's stable code (e.g. <see cref="VelocityRule.Code"/>), part of the published contract.</param>
/// <param name="Reason">Human-readable explanation, with the numbers that made it fire.</param>
public sealed record RuleViolation(string Rule, string Reason);
