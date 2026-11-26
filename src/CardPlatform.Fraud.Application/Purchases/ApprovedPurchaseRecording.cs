using CardPlatform.Fraud.Domain.Rules;

namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>What recording an approved purchase did.</summary>
/// <param name="Outcome">Whether the purchase was new (<see cref="RecordingOutcome.Recorded"/>) or already known.</param>
/// <param name="Suspicion">
/// The suspicion the new purchase raised, written to the outbox as a <c>FraudSuspected</c>; <see langword="null"/> when no
/// rule fired or the purchase was already recorded (it was judged then).
/// </param>
public sealed record ApprovedPurchaseRecording(RecordingOutcome Outcome, FraudSuspicion? Suspicion = null);
