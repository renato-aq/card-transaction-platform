using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// EF Core mapping of <see cref="ProcessedMessage"/> to <c>processed_messages</c>, applied by
/// <see cref="ProcessedMessageModelBuilderExtensions.ApplyProcessedMessages"/>. The table and column names are explicit
/// because <see cref="IntegrationEventProcessor{TDbContext}"/> writes the rows with SQL (they are the snake_case names
/// every service context uses anyway).
/// </summary>
public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public const string TableName = "processed_messages";

    public const string PrimaryKeyName = "pk_processed_messages";

    public const string EventIdColumn = "event_id";

    public const string TypeColumn = "type";

    public const string ProcessedAtColumn = "processed_at";

    public const int TypeMaxLength = 100;

    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        // The event id is the key: a second copy of the event conflicts with the first and is skipped (ADR 0011).
        builder.HasKey(message => message.EventId).HasName(PrimaryKeyName);
        builder.Property(message => message.EventId).HasColumnName(EventIdColumn).ValueGeneratedNever();
        builder.Property(message => message.Type).HasColumnName(TypeColumn).HasMaxLength(TypeMaxLength);

        // timestamptz; Npgsql only writes UTC offsets, so the processor stores it in UTC.
        builder.Property(message => message.ProcessedAt).HasColumnName(ProcessedAtColumn);
    }
}
