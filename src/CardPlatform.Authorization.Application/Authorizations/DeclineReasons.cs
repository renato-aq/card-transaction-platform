using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Domain.Authorizations;

namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// The stable <c>snake_case</c> codes of the <see cref="DeclineReason"/> values, exposed by the API as the
/// <c>declineReason</c> of a declined authorization.
/// </summary>
public static class DeclineReasons
{
    public const string CardBlockedCode = "card_blocked";

    public const string CurrencyMismatchCode = "currency_mismatch";

    /// <summary>Same code as the Ledger error it comes from (<see cref="LedgerErrors.InsufficientFundsCode"/>).</summary>
    public const string InsufficientFundsCode = LedgerErrors.InsufficientFundsCode;

    /// <summary>The code of <paramref name="reason"/>, or <see langword="null"/> when there is no reason.</summary>
    public static string? ToCode(DeclineReason? reason)
    {
        return reason switch
        {
            null => null,
            DeclineReason.CardBlocked => CardBlockedCode,
            DeclineReason.CurrencyMismatch => CurrencyMismatchCode,
            DeclineReason.InsufficientFunds => InsufficientFundsCode,
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown decline reason."),
        };
    }
}
