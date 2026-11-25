using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CardPlatform.Fraud.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> to create the context when adding migrations; it never opens a connection.
/// At runtime the context is registered by <see cref="DependencyInjection.AddFraudInfrastructure"/>.
/// </summary>
internal sealed class DesignTimeFraudDbContextFactory : IDesignTimeDbContextFactory<FraudDbContext>
{
    public FraudDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<FraudDbContext> options = new();
        options.UseNpgsql("Host=localhost;Database=frauddb");
        FraudDbContextOptions.ApplyConventions(options);
        return new FraudDbContext(options.Options);
    }
}
