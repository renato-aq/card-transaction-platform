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

    private const string AccountTransactionIdIndex = "ix_entries_account_id_transaction_id_id";

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

        // The order of the Settlement Job's file: it reads each merchant's entries in this order, a page at a time
        // starting after the last key read (keyset pagination: account_id = merchant and (transaction_id, id) > key), so
        // the index scan starts at the key and stops at the page's limit instead of sorting every entry of the merchant
        // or walking to the key from the merchant's first entry. The shadow foreign key to the transaction (configured with the transaction) is
        // declared here so that the index can name it.
        builder.Property<Guid>(TransactionIdColumn);
        builder.HasIndex(nameof(Entry.AccountId), TransactionIdColumn, IdColumn)
            .HasDatabaseName(AccountTransactionIdIndex);
    }
}
