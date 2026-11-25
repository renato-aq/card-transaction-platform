using CardPlatform.Ledger.Domain.Accounts;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardPlatform.Ledger.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedNever();

        builder.Property(account => account.Type)
            .HasConversion<string>()
            .HasMaxLength(LedgerConverters.EnumMaxLength);

        builder.Property(account => account.Currency)
            .HasColumnName("currency")
            .HasConversion(LedgerConverters.Currency)
            .HasColumnType(LedgerConverters.CurrencyColumnType);

        builder.Property(account => account.CreatedAt)
            .HasConversion(LedgerConverters.Utc);

        // The balance currency is always the account currency, so both share the "currency" column
        // instead of storing the same code twice.
        builder.ComplexProperty(account => account.Balance, balance =>
        {
            balance.Property(money => money.Amount)
                .HasColumnName("balance")
                .HasColumnType(LedgerConverters.AmountColumnType);

            balance.Property(money => money.Currency)
                .HasColumnName("currency")
                .HasConversion(LedgerConverters.Currency)
                .HasColumnType(LedgerConverters.CurrencyColumnType);
        });
    }
}
