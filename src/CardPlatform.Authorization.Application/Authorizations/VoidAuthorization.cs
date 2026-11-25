namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// Voids an approved authorization: its hold is voided in the Ledger, returning the money to the card, and the
/// authorization becomes <c>Voided</c>. See <see cref="VoidAuthorizationHandler"/> for the failures.
/// </summary>
public sealed record VoidAuthorization(Guid AuthorizationId);
