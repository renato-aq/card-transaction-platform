using CardPlatform.Ledger.Application.Abstractions.Messaging;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Accounts;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Monetary;

namespace CardPlatform.Ledger.Application.Holds;

/// <summary>
/// Handles <see cref="PlaceHold"/>. Failures are checked in this order and the first one is returned, so bad input is
/// rejected before touching the database:
/// <list type="number">
/// <item><c>invalid_hold_id</c>, <c>invalid_account_id</c> (card), <c>invalid_currency</c>, <c>invalid_amount</c>
/// (missing) and <c>too_many_decimal_places</c>, <c>invalid_amount</c> (not positive);</item>
/// <item>idempotency: when a hold with the id exists, it is returned as it is (<see cref="PlaceHoldResult.Created"/>
/// <see langword="false"/>, nothing written) if the card account and amount match, otherwise <c>hold_id_conflict</c>;</item>
/// <item><c>account_not_found</c> and <c>not_a_card_account</c> for the card account;</item>
/// <item>the rules of <see cref="Hold.Place"/>: <c>currency_mismatch</c>, <c>insufficient_funds</c>.</item>
/// </list>
/// The holds account of the currency is created on first use (ADR 0007). A single
/// <see cref="IUnitOfWork.SaveChangesAsync"/> writes the hold, its transaction and both balances. Two requests racing to
/// insert the same hold or the same holds account make the loser's save throw <see cref="ConcurrencyConflictException"/>;
/// the retry decorator runs it again and it then finds the existing hold or account.
/// </summary>
public sealed class PlaceHoldHandler(
    IHoldRepository holds,
    IAccountRepository accounts,
    ILedgerTransactionRepository transactions,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<PlaceHold, PlaceHoldResult>
{
    public async Task<Result<PlaceHoldResult>> HandleAsync(PlaceHold command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Id is not { } id || id == Guid.Empty)
        {
            return Result.Failure<PlaceHoldResult>(HoldErrors.InvalidHoldId);
        }

        if (command.CardAccountId is not { } cardAccountId || cardAccountId == Guid.Empty)
        {
            return Result.Failure<PlaceHoldResult>(HoldErrors.InvalidCardAccountId);
        }

        Result<Money> amount = ParseAmount(command.Amount, command.Currency);
        if (amount.IsFailure)
        {
            return Result.Failure<PlaceHoldResult>(amount.Error);
        }

        Hold? existing = await holds.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.CardAccountId == cardAccountId && existing.Amount == amount.Value
                ? Result.Success(new PlaceHoldResult(HoldDto.From(existing), Created: false))
                : Result.Failure<PlaceHoldResult>(HoldErrors.HoldIdConflict(id));
        }

        Account? card = await accounts.GetByIdAsync(cardAccountId, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return Result.Failure<PlaceHoldResult>(AccountErrors.AccountNotFound(cardAccountId));
        }

        if (card.Type != AccountType.Card)
        {
            return Result.Failure<PlaceHoldResult>(HoldErrors.NotACardAccount(card));
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Account holdsAccount = await GetOrOpenHoldsAccountAsync(amount.Value.Currency, now, cancellationToken)
            .ConfigureAwait(false);

        Result<HoldPlacement> placement = Hold.Place(id, card, holdsAccount, amount.Value, now);
        if (placement.IsFailure)
        {
            return Result.Failure<PlaceHoldResult>(placement.Error);
        }

        transactions.Add(placement.Value.Transaction);
        holds.Add(placement.Value.Hold);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new PlaceHoldResult(HoldDto.From(placement.Value.Hold), Created: true));
    }

    private static Result<Money> ParseAmount(decimal? amount, string? currencyCode)
    {
        Result<Currency> currency = Currency.Create(currencyCode);
        if (currency.IsFailure)
        {
            return Result.Failure<Money>(currency.Error);
        }

        if (amount is not { } value)
        {
            return Result.Failure<Money>(HoldErrors.MissingAmount);
        }

        Result<Money> money = Money.Create(value, currency.Value);
        if (money.IsFailure)
        {
            return money;
        }

        return value > 0m ? money : Result.Failure<Money>(HoldErrors.NonPositiveAmount(value));
    }

    // The holds account is a system account, one per currency, opened on first use (ADR 0007). It is only added here;
    // the save that places the hold inserts it, and nothing is written if placing fails.
    private async Task<Account> GetOrOpenHoldsAccountAsync(
        Currency currency,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Account? holdsAccount = await accounts.GetHoldsAccountAsync(currency, cancellationToken).ConfigureAwait(false);
        if (holdsAccount is not null)
        {
            return holdsAccount;
        }

        holdsAccount = Account.Open(AccountType.Holds, currency, now);
        accounts.Add(holdsAccount);
        return holdsAccount;
    }
}
