namespace CardPlatform.Authorization.Domain.Authorizations;

/// <summary>
/// Status of a <see cref="CardAuthorization"/>. A purchase is <see cref="Approved"/> (the amount is held in the Ledger)
/// or <see cref="Declined"/> (nothing is held); an approved authorization is later <see cref="Captured"/> or
/// <see cref="Voided"/>.
/// </summary>
public enum AuthorizationStatus
{
    Approved,
    Declined,
    Captured,
    Voided,
}
