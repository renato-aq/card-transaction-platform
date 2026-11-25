namespace CardPlatform.Contracts.Fraud;

/// <summary>
/// The Fraud Worker suspects a card is being used fraudulently; published on <see cref="Topics.FraudSignals"/>.
/// </summary>
/// <param name="CardId">The suspected card.</param>
/// <param name="AuthorizationId">The authorization whose event triggered the rule.</param>
/// <param name="Rule">Identifier of the rule that fired (e.g. a velocity rule).</param>
/// <param name="Reason">Human-readable explanation of why the rule fired.</param>
/// <param name="DetectedAt">When the suspicion was raised.</param>
public sealed record FraudSuspected(
    Guid CardId,
    Guid AuthorizationId,
    string Rule,
    string Reason,
    DateTimeOffset DetectedAt) : IIntegrationEvent;
