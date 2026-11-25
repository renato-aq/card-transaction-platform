using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Domain.Cards;

/// <summary>
/// A card known to the Authorization. Its money lives in its own card account in the Ledger
/// (<see cref="LedgerAccountId"/>), the source of truth for the balance; the card holds what the Authorization decides
/// on by itself, such as whether it is blocked.
/// </summary>
public sealed class Card
{
    private Card(Guid id, Guid ledgerAccountId, Currency currency, DateTimeOffset createdAt)
    {
        Id = id;
        LedgerAccountId = ledgerAccountId;
        Currency = currency;
        Status = CardStatus.Active;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    /// <summary>The card account opened for this card in the Ledger.</summary>
    public Guid LedgerAccountId { get; }

    /// <summary>Currency of the card, the same as its Ledger account.</summary>
    public Currency Currency { get; }

    public CardStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Creates an <see cref="CardStatus.Active"/> card for an already opened Ledger card account. The id is a
    /// time-ordered UUID v7 derived from <paramref name="createdAt"/>. An empty account id or a
    /// <see langword="null"/> currency is a programming error.
    /// </summary>
    public static Card Create(Guid ledgerAccountId, Currency currency, DateTimeOffset createdAt)
    {
        if (ledgerAccountId == Guid.Empty)
        {
            throw new ArgumentException("The Ledger account id cannot be empty.", nameof(ledgerAccountId));
        }

        ArgumentNullException.ThrowIfNull(currency);

        return new Card(Guid.CreateVersion7(createdAt), ledgerAccountId, currency, createdAt);
    }

    /// <summary>Blocks the card. Idempotent: blocking a card that is already blocked keeps it blocked.</summary>
    public void Block()
    {
        Status = CardStatus.Blocked;
    }
}
