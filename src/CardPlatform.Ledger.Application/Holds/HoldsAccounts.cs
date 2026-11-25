using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Holds;

namespace CardPlatform.Ledger.Application.Holds;

internal static class HoldsAccounts
{
    /// <summary>
    /// Loads the holds account of an existing hold's currency. Placing the hold created it, so a missing one is
    /// corrupted data and throws.
    /// </summary>
    public static async Task<Account> GetExistingAsync(
        IAccountRepository accounts,
        Hold hold,
        CancellationToken cancellationToken)
    {
        return await accounts.GetHoldsAccountAsync(hold.Amount.Currency, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"The {hold.Amount.Currency} holds account of hold {hold.Id} does not exist.");
    }
}
