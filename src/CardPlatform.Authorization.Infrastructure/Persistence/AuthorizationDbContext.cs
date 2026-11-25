using CardPlatform.Authorization.Application.Abstractions.Persistence;
using CardPlatform.Authorization.Domain.Authorizations;
using CardPlatform.Authorization.Domain.Cards;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Authorization.Infrastructure.Persistence;

/// <summary>
/// EF Core context of the Authorization. Mappings live in <c>IEntityTypeConfiguration</c> classes of this assembly;
/// the domain model has no EF attributes.
/// </summary>
public sealed class AuthorizationDbContext(DbContextOptions<AuthorizationDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Card> Cards => Set<Card>();

    public DbSet<CardAuthorization> Authorizations => Set<CardAuthorization>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthorizationDbContext).Assembly);
    }
}
