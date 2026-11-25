using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Accounts;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Holds;

/// <summary>
/// Handles <see cref="CaptureHold"/>. Failures are checked in this order and the first one is returned:
/// <c>invalid_account_id</c> (merchant), <c>hold_not_found</c>, <c>account_not_found</c> (merchant), then the rules of
/// <see cref="Hold.Capture"/>: <c>hold_not_pending</c>, <c>not_a_merchant_account</c>, <c>currency_mismatch</c>.
/// A single <see cref="IUnitOfWork.SaveChangesAsync"/> writes the hold, the capture transaction and both balances.
/// </summary>
public sealed class CaptureHoldHandler(
    IHoldRepository holds,
    IAccountRepository accounts,
    ILedgerTransactionRepository transactions,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<CaptureHold, HoldDto>
{
    public async Task<Result<HoldDto>> HandleAsync(CaptureHold command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.MerchantAccountId is not { } merchantAccountId || merchantAccountId == Guid.Empty)
        {
            return Result.Failure<HoldDto>(HoldErrors.InvalidMerchantAccountId);
        }

        Hold? hold = await holds.GetByIdAsync(command.HoldId, cancellationToken).ConfigureAwait(false);
        if (hold is null)
        {
            return Result.Failure<HoldDto>(HoldErrors.HoldNotFound(command.HoldId));
        }

        Account? merchant = await accounts.GetByIdAsync(merchantAccountId, cancellationToken).ConfigureAwait(false);
        if (merchant is null)
        {
            return Result.Failure<HoldDto>(AccountErrors.AccountNotFound(merchantAccountId));
        }

        Account holdsAccount = await HoldsAccounts
            .GetExistingAsync(accounts, hold, cancellationToken)
            .ConfigureAwait(false);

        Result<LedgerTransaction> capture = hold.Capture(holdsAccount, merchant, timeProvider.GetUtcNow());
        if (capture.IsFailure)
        {
            return Result.Failure<HoldDto>(capture.Error);
        }

        transactions.Add(capture.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(HoldDto.From(hold));
    }
}
