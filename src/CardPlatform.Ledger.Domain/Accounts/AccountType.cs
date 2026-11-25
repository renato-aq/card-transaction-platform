namespace CardPlatform.Ledger.Domain.Accounts;

/// <summary>
/// Role of an account in the ledger. Only <see cref="Card"/> accounts are forbidden from going negative.
/// </summary>
public enum AccountType
{
    /// <summary>Balance available to a cardholder; can never be negative.</summary>
    Card,

    /// <summary>Amount owed to a merchant for purchases not yet settled.</summary>
    Merchant,

    /// <summary>Platform account where money enters (top-ups) and leaves (settlements) the ledger.</summary>
    Settlement,

    /// <summary>
    /// Platform account, one per currency, holding the amounts reserved by pending authorization holds
    /// (see <see cref="Domain.Holds.Hold"/>); it can have any balance.
    /// </summary>
    Holds,
}
