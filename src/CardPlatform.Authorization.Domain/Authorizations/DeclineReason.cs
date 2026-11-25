namespace CardPlatform.Authorization.Domain.Authorizations;

/// <summary>Why a purchase was <see cref="AuthorizationStatus.Declined"/>.</summary>
public enum DeclineReason
{
    /// <summary>The card is blocked; the Ledger is not called.</summary>
    CardBlocked,

    /// <summary>The purchase is in a currency other than the card's; the Ledger is not called.</summary>
    CurrencyMismatch,

    /// <summary>The Ledger refused the hold because the card balance is lower than the amount.</summary>
    InsufficientFunds,
}
