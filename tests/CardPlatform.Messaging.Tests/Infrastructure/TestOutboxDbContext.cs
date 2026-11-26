using CardPlatform.Messaging.Consuming;
using CardPlatform.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Messaging.Tests.Infrastructure;

/// <summary>
/// A context with nothing but the outbox and the processed messages, as a service context maps them (snake_case,
/// PostgreSQL). The consumer tests' handlers write outbox rows as the effect of processing an event.
/// </summary>
public sealed class TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyOutbox();
        modelBuilder.ApplyProcessedMessages();
    }
}
