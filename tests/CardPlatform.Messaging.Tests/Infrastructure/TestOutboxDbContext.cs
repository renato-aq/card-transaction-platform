using CardPlatform.Messaging.Outbox;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Messaging.Tests.Infrastructure;

/// <summary>A context with nothing but the outbox, as a service context maps it (snake_case, PostgreSQL).</summary>
public sealed class TestOutboxDbContext(DbContextOptions<TestOutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyOutbox();
    }
}
