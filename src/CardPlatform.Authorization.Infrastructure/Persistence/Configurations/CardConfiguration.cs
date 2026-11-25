using CardPlatform.Authorization.Domain.Cards;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardPlatform.Authorization.Infrastructure.Persistence.Configurations;

internal sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    /// <summary>Unique index that ties each Ledger card account to a single card.</summary>
    public const string LedgerAccountIndex = "ux_cards_ledger_account_id";

    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.ToTable("cards");

        builder.HasKey(card => card.Id);
        builder.Property(card => card.Id).ValueGeneratedNever();

        builder.HasIndex(card => card.LedgerAccountId)
            .IsUnique()
            .HasDatabaseName(LedgerAccountIndex);

        builder.Property(card => card.Currency)
            .HasConversion(AuthorizationConverters.Currency)
            .HasColumnType(AuthorizationConverters.CurrencyColumnType);

        // Statuses are stored as their enum names (e.g. "Blocked").
        builder.Property(card => card.Status)
            .HasConversion<string>()
            .HasMaxLength(AuthorizationConverters.EnumMaxLength);

        builder.Property(card => card.CreatedAt)
            .HasConversion(AuthorizationConverters.Utc);
    }
}
