using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardPlatform.Ledger.Infrastructure.Persistence.Configurations;

internal sealed class HoldConfiguration : IEntityTypeConfiguration<Hold>
{
    public void Configure(EntityTypeBuilder<Hold> builder)
    {
        builder.ToTable("holds");

        // The id is given by the creator (the authorization id), never generated.
        builder.HasKey(hold => hold.Id);
        builder.Property(hold => hold.Id).ValueGeneratedNever();

        builder.ComplexProperty(hold => hold.Amount, amount =>
        {
            amount.Property(money => money.Amount)
                .HasColumnName("amount")
                .HasColumnType(LedgerConverters.AmountColumnType);

            amount.Property(money => money.Currency)
                .HasColumnName("currency")
                .HasConversion(LedgerConverters.Currency)
                .HasColumnType(LedgerConverters.CurrencyColumnType);
        });

        builder.Property(hold => hold.Status)
            .HasConversion<string>()
            .HasMaxLength(LedgerConverters.EnumMaxLength);

        builder.Property(hold => hold.CreatedAt)
            .HasConversion(LedgerConverters.Utc);

        builder.Property(hold => hold.CompletedAt)
            .HasConversion(LedgerConverters.Utc);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(hold => hold.CardAccountId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerTransaction>()
            .WithMany()
            .HasForeignKey(hold => hold.PlacementTransactionId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<LedgerTransaction>()
            .WithMany()
            .HasForeignKey(hold => hold.CompletionTransactionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency, as on accounts (ADR 0006): two requests capturing or voiding the same hold cannot
        // both succeed. Shadow property, so the domain model is unaware of it (ADR 0004).
        builder.Property<uint>(AccountConfiguration.RowVersion).IsRowVersion();
    }
}
