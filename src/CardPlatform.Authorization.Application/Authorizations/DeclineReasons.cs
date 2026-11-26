using CardPlatform.Authorization.Application.Abstractions.Ledger;
using CardPlatform.Authorization.Domain.Authorizations;

using ContractDeclineReasons = CardPlatform.Contracts.Authorizations.DeclineReasons;

namespace CardPlatform.Authorization.Application.Authorizations;

/// <summary>
/// The stable <c>snake_case</c> codes of the <see cref="DeclineReason"/> values, exposed by the API as the
/// <c>declineReason</c> of a declined authorization and carried by the <c>AuthorizationDeclined</c> event. The codes are
/// defined once, in the integration contracts (<see cref="ContractDeclineReasons"/>), so the API and the events agree.
/// </summary>
public static class DeclineReasons
{
    public const string CardBlockedCode = ContractDeclineReasons.CardBlocked;

    public const string CurrencyMismatchCode = ContractDeclineReasons.CurrencyMismatch;

    /// <summary>Same code as the Ledger error it comes from (<see cref="LedgerErrors.InsufficientFundsCode"/>).</summary>
    public const string InsufficientFundsCode = ContractDeclineReasons.InsufficientFunds;

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
