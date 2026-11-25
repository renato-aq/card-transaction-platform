using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Common;
using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Domain.Authorizations;

/// <summary>
/// The decision on a card purchase: <see cref="AuthorizationStatus.Approved"/>, with the amount held in the Ledger
/// under a hold whose id is this authorization's <see cref="Id"/> (ADR 0007), or
/// <see cref="AuthorizationStatus.Declined"/> with a <see cref="DeclineReason"/>. Declines are business outcomes, so
/// they are recorded too. An approved authorization is completed once, by <see cref="Capture"/> (the money goes to the
/// merchant) or <see cref="Void"/> (it goes back to the card); both states are final, like the hold's in the Ledger.
/// </summary>
/// <remarks>
/// Named <c>CardAuthorization</c> rather than <c>Authorization</c>, which would clash with the
/// <c>CardPlatform.Authorization</c> namespace.
/// </remarks>
public sealed class CardAuthorization
{
    public const string NotCapturableCode = "authorization_not_capturable";

    public const string NotVoidableCode = "authorization_not_voidable";

    private CardAuthorization(
        Guid id,
        Guid cardId,
        Guid merchantAccountId,
        Money amount,
        AuthorizationStatus status,
        DeclineReason? declineReason,
        DateTimeOffset createdAt)
    {
        Id = id;
        CardId = cardId;
        MerchantAccountId = merchantAccountId;
        Amount = amount;
        Status = status;
        DeclineReason = declineReason;
        CreatedAt = createdAt.TruncateToMicroseconds();
    }

    // Used only to rehydrate persisted authorizations: Amount is set right after construction.
    private CardAuthorization(
        Guid id,
        Guid cardId,
        Guid merchantAccountId,
        AuthorizationStatus status,
        DeclineReason? declineReason,
        DateTimeOffset createdAt)
    {
        Id = id;
        CardId = cardId;
        MerchantAccountId = merchantAccountId;
        Amount = null!;
        Status = status;
        DeclineReason = declineReason;
        CreatedAt = createdAt;
    }

    /// <summary>Also the id of the hold placed in the Ledger for an approved authorization.</summary>
    public Guid Id { get; }

    public Guid CardId { get; }

    /// <summary>The merchant's account in the Ledger, credited when the authorization is captured.</summary>
    public Guid MerchantAccountId { get; }

    public Money Amount { get; }

    public AuthorizationStatus Status { get; private set; }

    /// <summary>Why the purchase was declined; <see langword="null"/> unless it is declined.</summary>
    public DeclineReason? DeclineReason { get; }

    /// <summary>When the authorization was decided, in whole microseconds (see <see cref="Timestamps"/>).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// When the authorization was captured or voided, in whole microseconds (see <see cref="Timestamps"/>);
    /// <see langword="null"/> while it is approved or when declined.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// A new authorization id: a time-ordered UUID v7 derived from <paramref name="createdAt"/>. It is chosen before
    /// the Ledger is called, because it is also the id of the hold.
    /// </summary>
    public static Guid NewId(DateTimeOffset createdAt)
    {
        return Guid.CreateVersion7(createdAt);
    }

    /// <summary>
    /// The rules decided by the Authorization alone, checked in this order: the card must be active
    /// (<see cref="Authorizations.DeclineReason.CardBlocked"/>) and the amount must be in the card currency
    /// (<see cref="Authorizations.DeclineReason.CurrencyMismatch"/>). Returns <see langword="null"/> when both hold;
    /// the balance is then checked by the Ledger when the hold is placed.
    /// </summary>
    public static DeclineReason? CheckCard(Card card, Money amount)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(amount);

        if (card.Status != CardStatus.Active)
        {
            return Authorizations.DeclineReason.CardBlocked;
        }

        if (amount.Currency != card.Currency)
        {
            return Authorizations.DeclineReason.CurrencyMismatch;
        }

        return null;
    }

    /// <summary>
    /// An approved authorization, created once its hold was placed in the Ledger. Approving a purchase that
    /// <see cref="CheckCard"/> declines, an empty id or merchant account id, or a non-positive amount is a programming
    /// error and throws.
    /// </summary>
    public static CardAuthorization Approve(
        Guid id,
        Card card,
        Guid merchantAccountId,
        Money amount,
        DateTimeOffset createdAt)
    {
        EnsureValid(id, card, merchantAccountId, amount);

        if (CheckCard(card, amount) is { } reason)
        {
            throw new InvalidOperationException($"Card {card.Id} cannot be authorized: {reason}.");
        }

        return new CardAuthorization(
            id,
            card.Id,
            merchantAccountId,
            amount,
            AuthorizationStatus.Approved,
            declineReason: null,
            createdAt);
    }

    /// <summary>
    /// A declined authorization. An empty id or merchant account id, a non-positive amount or an unknown reason is a
    /// programming error and throws.
    /// </summary>
    public static CardAuthorization Decline(
        Guid id,
        Card card,
        Guid merchantAccountId,
        Money amount,
        DeclineReason reason,
        DateTimeOffset createdAt)
    {
        EnsureValid(id, card, merchantAccountId, amount);

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown decline reason.");
        }

        return new CardAuthorization(
            id,
            card.Id,
            merchantAccountId,
            amount,
            AuthorizationStatus.Declined,
            reason,
            createdAt);
    }

    /// <summary>
    /// Marks the authorization <see cref="AuthorizationStatus.Captured"/> at <paramref name="completedAt"/>, once its
    /// hold was captured in the Ledger. Only an <see cref="AuthorizationStatus.Approved"/> authorization can be
    /// captured; any other (declined, captured or voided) fails with <see cref="NotCapturableCode"/> and is unchanged.
    /// </summary>
    public Result Capture(DateTimeOffset completedAt)
    {
        Result capturable = EnsureCapturable();
        if (capturable.IsFailure)
        {
            return capturable;
        }

        Complete(AuthorizationStatus.Captured, completedAt);
        return Result.Success();
    }

    /// <summary>
    /// Marks the authorization <see cref="AuthorizationStatus.Voided"/> at <paramref name="completedAt"/>, once its hold
    /// was voided in the Ledger. Only an <see cref="AuthorizationStatus.Approved"/> authorization can be voided; any
    /// other (declined, captured or voided) fails with <see cref="NotVoidableCode"/> and is unchanged.
    /// </summary>
    public Result Void(DateTimeOffset completedAt)
    {
        Result voidable = EnsureVoidable();
        if (voidable.IsFailure)
        {
            return voidable;
        }

        Complete(AuthorizationStatus.Voided, completedAt);
        return Result.Success();
    }

    /// <summary>
    /// Whether <see cref="Capture"/> would succeed, without changing anything: fails with
    /// <see cref="NotCapturableCode"/> unless the authorization is <see cref="AuthorizationStatus.Approved"/>. Checked
    /// before the Ledger is called.
    /// </summary>
    public Result EnsureCapturable()
    {
        return Status == AuthorizationStatus.Approved
            ? Result.Success()
            : Result.Failure(Error.BusinessRule(
                NotCapturableCode,
                $"Authorization {Id} cannot be captured: it is {Status}, and only approved authorizations can be captured."));
    }

    /// <summary>
    /// Whether <see cref="Void"/> would succeed, without changing anything: fails with <see cref="NotVoidableCode"/>
    /// unless the authorization is <see cref="AuthorizationStatus.Approved"/>. Checked before the Ledger is called.
    /// </summary>
    public Result EnsureVoidable()
    {
        return Status == AuthorizationStatus.Approved
            ? Result.Success()
            : Result.Failure(Error.BusinessRule(
                NotVoidableCode,
                $"Authorization {Id} cannot be voided: it is {Status}, and only approved authorizations can be voided."));
    }

    private void Complete(AuthorizationStatus status, DateTimeOffset completedAt)
    {
        Status = status;
        CompletedAt = completedAt.TruncateToMicroseconds();
    }

    private static void EnsureValid(Guid id, Card card, Guid merchantAccountId, Money amount)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The authorization id cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(card);

        if (merchantAccountId == Guid.Empty)
        {
            throw new ArgumentException("The merchant account id cannot be empty.", nameof(merchantAccountId));
        }

        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount must be greater than zero.");
        }
    }
}
