using CardPlatform.Fraud.Application.Abstractions.Persistence;
using CardPlatform.Fraud.Domain.Purchases;
using CardPlatform.Messaging.Consuming;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Fraud.Infrastructure.Persistence;

/// <summary>
/// EF Core context of the Fraud Worker. Mappings live in <c>IEntityTypeConfiguration</c> classes of this assembly,
/// except the processed messages', shared through <c>CardPlatform.Messaging</c>; the domain model has no EF attributes.
/// </summary>
public sealed class FraudDbContext(DbContextOptions<FraudDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<CardPurchase> CardPurchases => Set<CardPurchase>();

    /// <summary>The authorization events already processed (ADR 0011).</summary>
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FraudDbContext).Assembly);
        modelBuilder.ApplyProcessedMessages();
    }
}
