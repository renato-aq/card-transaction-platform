using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Domain.Holds;

/// <summary>
/// Authorization hold (reservation) on a card account, modelled as double-entry transactions (ADR 0007):
/// placing it moves the amount from the card to the holds account of its currency, capturing it moves the amount
/// from the holds account to a merchant, and voiding it returns the amount to the card.
/// Each operation builds a <see cref="LedgerTransaction"/> and applies it with <see cref="LedgerTransaction.ApplyTo"/>,
/// so every ledger invariant (balanced, positive, single currency, card never negative, all or nothing) still holds.
/// The hold changes state only when its transaction was applied.
/// </summary>
public sealed class Hold
{
    public const string HoldNotPendingCode = "hold_not_pending";

    public const string NotAMerchantAccountCode = "not_a_merchant_account";

    private Hold(
        Guid id,
        Guid cardAccountId,
        Money amount,
        DateTimeOffset createdAt,
        Guid placementTransactionId)
    {
        Id = id;
        CardAccountId = cardAccountId;
        Amount = amount;
        Status = HoldStatus.Pending;
        CreatedAt = createdAt;
        PlacementTransactionId = placementTransactionId;
    }

    // Used only to rehydrate persisted holds: Amount is set right after construction.
    private Hold(
        Guid id,
        Guid cardAccountId,
        HoldStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt,
        Guid placementTransactionId,
        Guid? completionTransactionId)
    {
        Id = id;
        CardAccountId = cardAccountId;
        Amount = null!;
        Status = status;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
        PlacementTransactionId = placementTransactionId;
        CompletionTransactionId = completionTransactionId;
    }

    /// <summary>Id given by the creator (the Authorization service uses the authorization id).</summary>
    public Guid Id { get; }

    public Guid CardAccountId { get; }

    /// <summary>The reserved amount; a capture or void always moves this whole amount.</summary>
    public Money Amount { get; }

    public HoldStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the hold was captured or voided; <see langword="null"/> while it is pending.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>The transaction that moved the amount from the card to the holds account.</summary>
    public Guid PlacementTransactionId { get; }

    /// <summary>The capture or void transaction; <see langword="null"/> while the hold is pending.</summary>
    public Guid? CompletionTransactionId { get; private set; }

    /// <summary>
    /// Places a hold: debits <paramref name="cardAccount"/> and credits <paramref name="holdsAccount"/> by
    /// <paramref name="amount"/>. Returns the pending hold and the applied transaction, which the caller persists
    /// together with both accounts. Failures come from the ledger rules: <c>invalid_amount</c> (amount not positive),
    /// <c>currency_mismatch</c> (an account is not in the amount's currency) and <c>insufficient_funds</c> (the card
    /// would go negative); no balance changes on failure.
    /// </summary>
    /// <remarks>
    /// The caller chooses both accounts, so an empty <paramref name="id"/>, a <paramref name="cardAccount"/> that is not a
    /// <see cref="AccountType.Card"/> account or a <paramref name="holdsAccount"/> that is not a
    /// <see cref="AccountType.Holds"/> account is a programming error and throws <see cref="ArgumentException"/>.
    /// </remarks>
    public static Result<HoldPlacement> Place(
        Guid id,
        Account cardAccount,
        Account holdsAccount,
        Money amount,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Hold id must not be empty.", nameof(id));
        }

        EnsureType(cardAccount, AccountType.Card, nameof(cardAccount));
        EnsureType(holdsAccount, AccountType.Holds, nameof(holdsAccount));
        ArgumentNullException.ThrowIfNull(amount);

        Result<LedgerTransaction> transaction = Move(
            cardAccount,
            holdsAccount,
            amount,
            createdAt,
            $"Hold {id} placed");
        if (transaction.IsFailure)
        {
            return Result.Failure<HoldPlacement>(transaction.Error);
        }

        Hold hold = new(id, cardAccount.Id, amount, createdAt, transaction.Value.Id);
        return Result.Success(new HoldPlacement(hold, transaction.Value));
    }

    /// <summary>
    /// Captures the hold: debits <paramref name="holdsAccount"/> and credits <paramref name="merchantAccount"/> by the
    /// reserved amount, and marks the hold <see cref="HoldStatus.Captured"/>. Rules are checked in this order:
    /// <c>hold_not_pending</c>, <c>not_a_merchant_account</c> (the merchant account comes from the client), then the ledger
    /// rules (<c>currency_mismatch</c>). Nothing changes on failure.
    /// </summary>
    /// <remarks>
    /// A <paramref name="holdsAccount"/> that is not a <see cref="AccountType.Holds"/> account is a programming error and
    /// throws <see cref="ArgumentException"/>.
    /// </remarks>
    public Result<LedgerTransaction> Capture(Account holdsAccount, Account merchantAccount, DateTimeOffset capturedAt)
    {
        EnsureType(holdsAccount, AccountType.Holds, nameof(holdsAccount));
        ArgumentNullException.ThrowIfNull(merchantAccount);

        if (Status != HoldStatus.Pending)
        {
            return Result.Failure<LedgerTransaction>(NotPending("captured"));
        }

        if (merchantAccount.Type != AccountType.Merchant)
        {
            return Result.Failure<LedgerTransaction>(Error.BusinessRule(
                NotAMerchantAccountCode,
                $"Account {merchantAccount.Id} is a {merchantAccount.Type} account; a hold can only be captured to a Merchant account."));
        }

        return Complete(holdsAccount, merchantAccount, capturedAt, HoldStatus.Captured, $"Hold {Id} captured");
    }

    /// <summary>
    /// Voids the hold: debits <paramref name="holdsAccount"/> and credits <paramref name="cardAccount"/> by the reserved
    /// amount, and marks the hold <see cref="HoldStatus.Voided"/>. Rules are checked in this order:
    /// <c>hold_not_pending</c>, then the ledger rules (<c>currency_mismatch</c>). Nothing changes on failure.
    /// </summary>
    /// <remarks>
    /// The caller loads the accounts, so a <paramref name="holdsAccount"/> that is not a <see cref="AccountType.Holds"/>
    /// account, or a <paramref name="cardAccount"/> other than the hold's card account, is a programming error and throws
    /// <see cref="ArgumentException"/>.
    /// </remarks>
    public Result<LedgerTransaction> Void(Account holdsAccount, Account cardAccount, DateTimeOffset voidedAt)
    {
        EnsureType(holdsAccount, AccountType.Holds, nameof(holdsAccount));
        ArgumentNullException.ThrowIfNull(cardAccount);
        if (cardAccount.Id != CardAccountId)
        {
            throw new ArgumentException(
                $"Account {cardAccount.Id} is not the card account {CardAccountId} of hold {Id}.",
                nameof(cardAccount));
        }

        if (Status != HoldStatus.Pending)
        {
            return Result.Failure<LedgerTransaction>(NotPending("voided"));
        }

        return Complete(holdsAccount, cardAccount, voidedAt, HoldStatus.Voided, $"Hold {Id} voided");
    }

    private Result<LedgerTransaction> Complete(
        Account holdsAccount,
        Account destination,
        DateTimeOffset completedAt,
        HoldStatus finalStatus,
        string description)
    {
        Result<LedgerTransaction> transaction = Move(holdsAccount, destination, Amount, completedAt, description);
        if (transaction.IsFailure)
        {
            return transaction;
        }

        Status = finalStatus;
        CompletedAt = completedAt;
        CompletionTransactionId = transaction.Value.Id;
        return transaction;
    }

    private Error NotPending(string operation)
    {
        return Error.BusinessRule(
            HoldNotPendingCode,
            $"Hold {Id} is {Status} and cannot be {operation}; only a Pending hold can be.");
    }

    // Builds the two-entry transaction and applies it; the accounts are untouched when it fails.
    private static Result<LedgerTransaction> Move(
        Account from,
        Account to,
        Money amount,
        DateTimeOffset at,
        string description)
    {
        Result<LedgerTransaction> transaction = LedgerTransaction.Create(
            [Entry.Debit(from.Id, amount), Entry.Credit(to.Id, amount)],
            at,
            description);
        if (transaction.IsFailure)
        {
            return transaction;
        }

        Result applied = transaction.Value.ApplyTo([from, to]);
        return applied.IsSuccess ? transaction : Result.Failure<LedgerTransaction>(applied.Error);
    }

    private static void EnsureType(Account account, AccountType expected, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(account, parameterName);
        if (account.Type != expected)
        {
            throw new ArgumentException(
                $"Account {account.Id} is a {account.Type} account, but a {expected} account is required.",
                parameterName);
        }
    }
}
