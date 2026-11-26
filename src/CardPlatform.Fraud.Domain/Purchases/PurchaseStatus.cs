namespace CardPlatform.Fraud.Domain.Purchases;

/// <summary>Where a purchase stands, as last decided by the Authorization.</summary>
public enum PurchaseStatus
{
    /// <summary>Authorized: the amount is held on the card.</summary>
    Approved,

    /// <summary>Refused by the Authorization (see <see cref="CardPurchase.DeclineReason"/>).</summary>
    Declined,

    /// <summary>Approved and then captured: the merchant was paid.</summary>
    Captured,

    /// <summary>Approved and then voided: the hold was released.</summary>
    Voided,
}
