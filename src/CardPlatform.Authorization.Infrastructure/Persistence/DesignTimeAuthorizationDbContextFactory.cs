using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CardPlatform.Authorization.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> to create the context when adding migrations; it never opens a connection.
/// At runtime the context is registered by <see cref="DependencyInjection.AddAuthorizationInfrastructure"/>.
/// </summary>
internal sealed class DesignTimeAuthorizationDbContextFactory : IDesignTimeDbContextFactory<AuthorizationDbContext>
{
    public AuthorizationDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<AuthorizationDbContext> options = new();
        options.UseNpgsql("Host=localhost;Database=authorizationdb");
        AuthorizationDbContextOptions.ApplyConventions(options);
        return new AuthorizationDbContext(options.Options);
    }
}
