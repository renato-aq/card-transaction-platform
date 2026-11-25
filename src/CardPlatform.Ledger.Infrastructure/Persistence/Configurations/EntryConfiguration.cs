using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Transactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardPlatform.Ledger.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="Entry"/> has no identity in the domain, so the table uses a shadow surrogate key and a
/// shadow foreign key to its transaction.
/// </summary>
internal sealed class EntryConfiguration : IEntityTypeConfiguration<Entry>
{
    public const string TransactionIdColumn = "TransactionId";

    private const string IdColumn = "Id";

    public void Configure(EntityTypeBuilder<Entry> builder)
    {
        builder.ToTable("entries");

        builder.Property<long>(IdColumn).UseIdentityAlwaysColumn();
        builder.HasKey(IdColumn);

        builder.Property(entry => entry.Direction)
            .HasConversion<string>()
            .HasMaxLength(LedgerConverters.EnumMaxLength);

        builder.ComplexProperty(entry => entry.Amount, amount =>
        {
            amount.Property(money => money.Amount)
                .HasColumnName("amount")
                .HasColumnType(LedgerConverters.AmountColumnType);

            amount.Property(money => money.Currency)
                .HasColumnName("currency")
                .HasConversion(LedgerConverters.Currency)
                .HasColumnType(LedgerConverters.CurrencyColumnType);
        });

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(entry => entry.AccountId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
