using CardPlatform.Authorization.Domain.Cards;
using CardPlatform.Authorization.Domain.Monetary;

namespace CardPlatform.Authorization.Domain.Tests.Cards;

public class CardTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    private static readonly Currency Brl = Currency.Create("BRL").Value;

    [Fact]
    public void Create_returns_an_active_card_linked_to_its_ledger_account()
    {
        Guid ledgerAccountId = Guid.NewGuid();

        Card card = Card.Create(ledgerAccountId, Brl, CreatedAt);

        Assert.NotEqual(Guid.Empty, card.Id);
        Assert.Equal(7, card.Id.Version);
        Assert.Equal(ledgerAccountId, card.LedgerAccountId);
        Assert.Equal(Brl, card.Currency);
        Assert.Equal(CardStatus.Active, card.Status);
        Assert.Equal(CreatedAt, card.CreatedAt);
    }

    [Fact]
    public void Create_truncates_the_creation_time_to_microseconds()
    {
        // A Linux clock ticks every 100 ns; PostgreSQL keeps whole microseconds.
        Card card = Card.Create(Guid.NewGuid(), Brl, CreatedAt.AddTicks(7));

        Assert.Equal(CreatedAt, card.CreatedAt);
    }

    [Fact]
    public void Create_with_empty_ledger_account_id_throws()
    {
        Assert.Throws<ArgumentException>(() => Card.Create(Guid.Empty, Brl, CreatedAt));
    }

    [Fact]
    public void Create_with_null_currency_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Card.Create(Guid.NewGuid(), null!, CreatedAt));
    }

    [Fact]
    public void Block_sets_the_status_to_blocked()
    {
        Card card = Card.Create(Guid.NewGuid(), Brl, CreatedAt);

        card.Block();

        Assert.Equal(CardStatus.Blocked, card.Status);
    }

    [Fact]
    public void Block_is_idempotent()
    {
        Card card = Card.Create(Guid.NewGuid(), Brl, CreatedAt);
        card.Block();

        card.Block();

        Assert.Equal(CardStatus.Blocked, card.Status);
    }
}
