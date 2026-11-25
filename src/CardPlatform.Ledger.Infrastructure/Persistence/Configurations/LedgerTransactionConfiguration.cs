using CardPlatform.Ledger.Domain.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardPlatform.Ledger.Infrastructure.Persistence.Configurations;

internal sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Id).ValueGeneratedNever();

        builder.Property(transaction => transaction.Description);

        builder.Property(transaction => transaction.CreatedAt)
            .HasConversion(LedgerConverters.Utc);

        // Derived from the entries, which store their own currency.
        builder.Ignore(transaction => transaction.Currency);

        builder.HasMany(transaction => transaction.Entries)
            .WithOne()
            .HasForeignKey(EntryConfiguration.TransactionIdColumn)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(transaction => transaction.Entries)
            .HasField("_entries")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}
