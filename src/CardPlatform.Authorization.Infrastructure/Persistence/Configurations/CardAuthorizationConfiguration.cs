using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardPlatform.Authorization.Infrastructure.Persistence.Configurations;

internal sealed class CardAuthorizationConfiguration : IEntityTypeConfiguration<CardAuthorization>
{
    /// <summary>Maximum length of a stored <see cref="DeclineReason"/> name.</summary>
    public const int DeclineReasonMaxLength = 30;

    /// <summary>
    /// Primary key of <c>authorizations</c>; two requests with the same idempotency key (same reserved id) violate it.
    /// </summary>
    public const string PrimaryKeyName = "pk_authorizations";

    public void Configure(EntityTypeBuilder<CardAuthorization> builder)
    {
        builder.ToTable("authorizations");

        // The id is chosen by the Application before the Ledger is called (it is also the hold id), never generated.
        builder.HasKey(authorization => authorization.Id).HasName(PrimaryKeyName);
        builder.Property(authorization => authorization.Id).ValueGeneratedNever();

        builder.HasOne<Card>()
            .WithMany()
            .HasForeignKey(authorization => authorization.CardId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        // The merchant account lives in the Ledger, so it is a plain column with no foreign key.
        builder.Property(authorization => authorization.MerchantAccountId);

        builder.ComplexProperty(authorization => authorization.Amount, amount =>
        {
            amount.Property(money => money.Amount)
                .HasColumnName("amount")
                .HasColumnType(AuthorizationConverters.AmountColumnType);

            amount.Property(money => money.Currency)
                .HasColumnName("currency")
                .HasConversion(AuthorizationConverters.Currency)
                .HasColumnType(AuthorizationConverters.CurrencyColumnType);
        });

        // Statuses and decline reasons are stored as their enum names (e.g. "Declined", "CardBlocked").
        builder.Property(authorization => authorization.Status)
            .HasConversion<string>()
            .HasMaxLength(AuthorizationConverters.EnumMaxLength);

        builder.Property(authorization => authorization.DeclineReason)
            .HasConversion<string>()
            .HasMaxLength(DeclineReasonMaxLength);

        builder.Property(authorization => authorization.CreatedAt)
            .HasConversion(AuthorizationConverters.Utc);
    }
}
