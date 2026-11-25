namespace CardPlatform.Ledger.Domain.Holds;

/// <summary>
/// State of a <see cref="Hold"/>. A hold starts <see cref="Pending"/> and ends either <see cref="Captured"/> or
/// <see cref="Voided"/>; both final states are terminal.
/// </summary>
public enum HoldStatus
{
    /// <summary>The amount is reserved: debited from the card and credited to the holds account.</summary>
    Pending,

    /// <summary>The reserved amount was moved from the holds account to the merchant.</summary>
    Captured,

    /// <summary>The reserved amount was returned from the holds account to the card.</summary>
    Voided,
}
