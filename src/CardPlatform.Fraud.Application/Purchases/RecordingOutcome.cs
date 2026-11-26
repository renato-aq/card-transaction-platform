namespace CardPlatform.Fraud.Application.Purchases;

/// <summary>What recording an Authorization decision changed in a card's history.</summary>
public enum RecordingOutcome
{
    /// <summary>The purchase was new and was added to the history.</summary>
    Recorded,

    /// <summary>The purchase was known and its status changed (captured or voided).</summary>
    Updated,

    /// <summary>The history already said so (the same decision arrived again); nothing changed.</summary>
    AlreadyRecorded,
}
