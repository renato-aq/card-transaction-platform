using CardPlatform.Fraud.Domain.Common;

namespace CardPlatform.Fraud.Domain.Purchases;

/// <summary>
/// A purchase made with a card, as decided by the Authorization and learned from its events: the history the fraud
/// rules look at. Identified by the authorization id, so a purchase is recorded once however many times its events
/// arrive. An approved purchase may later be <see cref="Capture">captured</see> or <see cref="Void">voided</see>; a
/// declined one is recorded too, with its reason, and never changes.
/// </summary>
public sealed class CardPurchase
{
    public const string InvalidPurchaseCode = "invalid_purchase";

    public const string NotCompletableCode = "purchase_not_completable";

    private CardPurchase(
        Guid id,
        Guid cardId,
        Guid merchantAccountId,
        Money amount,
        PurchaseStatus status,
        string? declineReason,
        DateTimeOffset authorizedAt)
    {
        Id = id;
        CardId = cardId;
        MerchantAccountId = merchantAccountId;
        Amount = amount;
        Status = status;
        DeclineReason = declineReason;
        AuthorizedAt = authorizedAt.TruncateToMicroseconds();
    }

    // Used only to rehydrate persisted purchases: Amount is set right after construction.
    private CardPurchase(
        Guid id,
        Guid cardId,
        Guid merchantAccountId,
        PurchaseStatus status,
        string? declineReason,
        DateTimeOffset authorizedAt)
    {
        Id = id;
        CardId = cardId;
        MerchantAccountId = merchantAccountId;
        Amount = null!;
        Status = status;
        DeclineReason = declineReason;
        AuthorizedAt = authorizedAt;
    }

    /// <summary>The authorization id (UUID v7), unique per purchase.</summary>
    public Guid Id { get; }

    public Guid CardId { get; }

    /// <summary>The merchant, identified by its Ledger account id.</summary>
    public Guid MerchantAccountId { get; }

    public Money Amount { get; }

    public PurchaseStatus Status { get; private set; }

    /// <summary>
    /// Why the purchase was declined, as the Authorization's snake_case code (e.g. <c>insufficient_funds</c>), kept as
    /// text so a reason this version does not know is still recorded; <see langword="null"/> unless declined.
    /// </summary>
    public string? DeclineReason { get; }

    /// <summary>When the Authorization decided the purchase, in whole microseconds (see <see cref="Timestamps"/>).</summary>
    public DateTimeOffset AuthorizedAt { get; }

    /// <summary>When it was captured or voided, in whole microseconds; <see langword="null"/> until then.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// Whether the Authorization approved the purchase, whatever happened to it afterwards (captured, voided): what the
    /// fraud rules count.
    /// </summary>
    public bool WasApproved => Status != PurchaseStatus.Declined;

    /// <summary>An approved purchase. Fails with <see cref="InvalidPurchaseCode"/> when an id is empty.</summary>
    public static Result<CardPurchase> Approve(
        Guid authorizationId,
        Guid cardId,
        Guid merchantAccountId,
        Money amount,
        DateTimeOffset authorizedAt)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (Validate(authorizationId, cardId, merchantAccountId) is { } error)
        {
            return Result.Failure<CardPurchase>(error);
        }

        return Result.Success(new CardPurchase(
            authorizationId, cardId, merchantAccountId, amount, PurchaseStatus.Approved, declineReason: null, authorizedAt));
    }

    /// <summary>
    /// A declined purchase. Fails with <see cref="InvalidPurchaseCode"/> when an id or the reason is empty.
    /// </summary>
    public static Result<CardPurchase> Decline(
        Guid authorizationId,
        Guid cardId,
        Guid merchantAccountId,
        Money amount,
        string reason,
        DateTimeOffset authorizedAt)
    {
        ArgumentNullException.ThrowIfNull(amount);

        if (Validate(authorizationId, cardId, merchantAccountId) is { } error)
        {
            return Result.Failure<CardPurchase>(error);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure<CardPurchase>(Error.Validation(
                InvalidPurchaseCode, $"Declined purchase {authorizationId} has no decline reason."));
        }

        return Result.Success(new CardPurchase(
            authorizationId, cardId, merchantAccountId, amount, PurchaseStatus.Declined, reason, authorizedAt));
    }

    /// <summary>
    /// Marks the approved purchase <see cref="PurchaseStatus.Captured"/> at <paramref name="completedAt"/>. Capturing it
    /// again changes nothing (the event may arrive twice); a declined or voided purchase fails with
    /// <see cref="NotCompletableCode"/> and is unchanged.
    /// </summary>
    public Result Capture(DateTimeOffset completedAt)
    {
        return Complete(PurchaseStatus.Captured, completedAt);
    }

    /// <summary>
    /// Marks the approved purchase <see cref="PurchaseStatus.Voided"/> at <paramref name="completedAt"/>. Voiding it again
    /// changes nothing (the event may arrive twice); a declined or captured purchase fails with
    /// <see cref="NotCompletableCode"/> and is unchanged.
    /// </summary>
    public Result Void(DateTimeOffset completedAt)
    {
        return Complete(PurchaseStatus.Voided, completedAt);
    }

    private Result Complete(PurchaseStatus status, DateTimeOffset completedAt)
    {
        if (Status == status)
        {
            return Result.Success();
        }

        if (Status != PurchaseStatus.Approved)
        {
            return Result.Failure(Error.Conflict(
                NotCompletableCode,
                $"Purchase {Id} cannot become {status}: it is {Status}, and only approved purchases can be completed."));
        }

        Status = status;
        CompletedAt = completedAt.TruncateToMicroseconds();
        return Result.Success();
    }

    private static Error? Validate(Guid authorizationId, Guid cardId, Guid merchantAccountId)
    {
        return authorizationId == Guid.Empty || cardId == Guid.Empty || merchantAccountId == Guid.Empty
            ? Error.Validation(InvalidPurchaseCode, "A purchase needs an authorization id, a card id and a merchant account id.")
            : null;
    }
}
