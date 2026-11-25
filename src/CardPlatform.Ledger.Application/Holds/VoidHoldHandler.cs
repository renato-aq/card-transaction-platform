using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Holds;

/// <summary>
/// Handles <see cref="VoidHold"/>. Failures are checked in this order and the first one is returned:
/// <c>hold_not_found</c>, then the rules of <see cref="Hold.Void"/>: <c>hold_not_pending</c>, <c>currency_mismatch</c>.
/// A single <see cref="IUnitOfWork.SaveChangesAsync"/> writes the hold, the void transaction and both balances.
/// </summary>
public sealed class VoidHoldHandler(
    IHoldRepository holds,
    IAccountRepository accounts,
    ILedgerTransactionRepository transactions,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<VoidHold, HoldDto>
{
    public async Task<Result<HoldDto>> HandleAsync(VoidHold command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        Hold? hold = await holds.GetByIdAsync(command.HoldId, cancellationToken).ConfigureAwait(false);
        if (hold is null)
        {
            return Result.Failure<HoldDto>(HoldErrors.HoldNotFound(command.HoldId));
        }

        // The card account is referenced by the hold (foreign key), so it always exists.
        Account card = await accounts.GetByIdAsync(hold.CardAccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Card account {hold.CardAccountId} of hold {hold.Id} does not exist.");
        Account holdsAccount = await HoldsAccounts
            .GetExistingAsync(accounts, hold, cancellationToken)
            .ConfigureAwait(false);

        Result<LedgerTransaction> voided = hold.Void(holdsAccount, card, timeProvider.GetUtcNow());
        if (voided.IsFailure)
        {
            return Result.Failure<HoldDto>(voided.Error);
        }

        transactions.Add(voided.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(HoldDto.From(hold));
    }
}
