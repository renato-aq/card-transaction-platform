using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Fraud.Infrastructure.Persistence;

/// <summary>
/// EF Core context of the Fraud Worker. Mappings live in <c>IEntityTypeConfiguration</c> classes of this assembly;
/// the domain model has no EF attributes. There are no entities yet.
/// </summary>
public sealed class FraudDbContext(DbContextOptions<FraudDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FraudDbContext).Assembly);
    }
}
