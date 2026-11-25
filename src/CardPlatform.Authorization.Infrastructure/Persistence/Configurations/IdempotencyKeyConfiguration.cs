using CardPlatform.Authorization.Application.Idempotency;
using CardPlatform.Authorization.Infrastructure.Persistence.Idempotency;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardPlatform.Authorization.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKeyRecord>
{
    public const string TableName = "idempotency_keys";

    /// <summary>Primary key of <c>idempotency_keys</c>: it makes each key unique, so only one request can claim it.</summary>
    public const string PrimaryKeyName = "pk_idempotency_keys";

    /// <summary>Length of a SHA-256 hash in hex.</summary>
    public const int RequestHashLength = 64;

    public void Configure(EntityTypeBuilder<IdempotencyKeyRecord> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(record => record.Key).HasName(PrimaryKeyName);
        builder.Property(record => record.Key).HasMaxLength(IdempotentAuthorizePurchaseHandler.MaxKeyLength);

        builder.Property(record => record.RequestHash)
            .HasMaxLength(RequestHashLength)
            .IsFixedLength();

        // Completing a key only succeeds while the request still holds the claim (UPDATE ... WHERE claim_token = ...).
        builder.Property(record => record.ClaimToken).IsConcurrencyToken();

        builder.Property(record => record.LockedUntil).HasConversion(AuthorizationConverters.Utc);

        builder.Property(record => record.ResponseBody).HasColumnType("text");

        builder.Property(record => record.CreatedAt).HasConversion(AuthorizationConverters.Utc);

        builder.Property(record => record.CompletedAt).HasConversion(AuthorizationConverters.Utc);
    }
}
