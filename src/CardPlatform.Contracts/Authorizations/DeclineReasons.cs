namespace CardPlatform.Contracts.Authorizations;

/// <summary>
/// Known values of <see cref="AuthorizationDeclined.Reason"/>, the same codes the Authorization API returns. Kept as
/// strings so a new reason does not break consumers that do not know it yet.
/// </summary>
public static class DeclineReasons
{
    public const string CardBlocked = "card_blocked";

    public const string CurrencyMismatch = "currency_mismatch";

    public const string InsufficientFunds = "insufficient_funds";
}
