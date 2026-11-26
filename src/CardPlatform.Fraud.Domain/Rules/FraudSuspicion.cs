namespace CardPlatform.Fraud.Domain.Rules;

/// <summary>
/// The suspicion raised by one purchase: every rule it fired, at least one. A purchase raises at most one suspicion,
/// however many rules fire, so the rules and their reasons are combined (<see cref="Rule"/>, <see cref="Reason"/>).
/// </summary>
public sealed class FraudSuspicion
{
    /// <summary>Separates the rule codes in <see cref="Rule"/> (codes never contain it).</summary>
    public const string RuleSeparator = ",";

    /// <summary>Separates the reasons in <see cref="Reason"/>.</summary>
    public const string ReasonSeparator = " ";

    public FraudSuspicion(Guid cardId, Guid authorizationId, IEnumerable<RuleViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);

        RuleViolation[] all = [.. violations];
        if (all.Length == 0)
        {
            throw new ArgumentException("A suspicion needs at least one rule that fired.", nameof(violations));
        }

        CardId = cardId;
        AuthorizationId = authorizationId;
        Violations = all;
    }

    public Guid CardId { get; }

    /// <summary>The purchase (its authorization id) that fired the rules.</summary>
    public Guid AuthorizationId { get; }

    /// <summary>The rules that fired, in the order they are evaluated.</summary>
    public IReadOnlyList<RuleViolation> Violations { get; }

    /// <summary>The codes of the rules that fired, comma-separated, e.g. <c>velocity,amount_above_average</c>.</summary>
    public string Rule => string.Join(RuleSeparator, Violations.Select(static violation => violation.Rule));

    /// <summary>The reasons of the rules that fired, in the same order as <see cref="Rule"/>, one sentence each.</summary>
    public string Reason => string.Join(ReasonSeparator, Violations.Select(static violation => violation.Reason));
}
