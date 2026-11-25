using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Authorization.Infrastructure.Persistence;

/// <summary>
/// EF Core context of the Authorization. Mappings live in <c>IEntityTypeConfiguration</c> classes of this assembly;
/// the domain model has no EF attributes. There are no entities yet.
/// </summary>
public sealed class AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthorizationDbContext).Assembly);
    }
}
