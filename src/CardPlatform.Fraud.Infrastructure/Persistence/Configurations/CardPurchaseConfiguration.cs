using CardPlatform.Fraud.Domain.Purchases;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CardPlatform.Fraud.Infrastructure.Persistence.Configurations;

internal sealed class CardPurchaseConfiguration : IEntityTypeConfiguration<CardPurchase>
{
    public const string TableName = "card_purchases";

    /// <summary>Index of a card's purchases by time: the history the fraud rules read.</summary>
    public const string CardHistoryIndexName = "ix_card_purchases_card_id_authorized_at";

    /// <summary>Column type for amounts: two decimal places, as enforced by <see cref="Money"/>.</summary>
    public const string AmountColumnType = "numeric(18,2)";

    /// <summary>Column type for ISO 4217 currency codes.</summary>
    public const string CurrencyColumnType = "char(3)";

    public const int StatusMaxLength = 20;

    public const int DeclineReasonMaxLength = 50;

    /// <summary>
    /// PostgreSQL <c>timestamptz</c> only accepts UTC offsets in Npgsql, so instants are stored in UTC (the instant is
    /// preserved; the original offset is not).
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Utc = new(
        value => value.ToUniversalTime(),
        value => value);

    public void Configure(EntityTypeBuilder<CardPurchase> builder)
    {
        builder.ToTable(TableName);

        // The authorization id, chosen by the Authorization, identifies the purchase.
        builder.HasKey(purchase => purchase.Id);
        builder.Property(purchase => purchase.Id).ValueGeneratedNever();

        // Cards and merchant accounts live in other services, so these are plain columns.
        builder.Property(purchase => purchase.CardId);
        builder.Property(purchase => purchase.MerchantAccountId);

        builder.ComplexProperty(purchase => purchase.Amount, amount =>
        {
            amount.Property(money => money.Amount).HasColumnName("amount").HasColumnType(AmountColumnType);
            amount.Property(money => money.Currency).HasColumnName("currency").HasColumnType(CurrencyColumnType);
        });

        // Stored as the enum name (e.g. "Captured").
        builder.Property(purchase => purchase.Status).HasConversion<string>().HasMaxLength(StatusMaxLength);
        builder.Property(purchase => purchase.DeclineReason).HasMaxLength(DeclineReasonMaxLength);

        builder.Property(purchase => purchase.AuthorizedAt).HasConversion(Utc);
        builder.Property(purchase => purchase.CompletedAt).HasConversion(Utc);

        builder.Ignore(purchase => purchase.WasApproved);

        builder.HasIndex(purchase => new { purchase.CardId, purchase.AuthorizedAt }).HasDatabaseName(CardHistoryIndexName);
    }
}
