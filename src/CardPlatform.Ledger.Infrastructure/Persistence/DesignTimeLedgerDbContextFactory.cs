using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CardPlatform.Ledger.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> to create the context when adding migrations; it never opens a connection.
/// At runtime the context is registered by <see cref="DependencyInjection.AddLedgerInfrastructure"/>.
/// </summary>
internal sealed class DesignTimeLedgerDbContextFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<LedgerDbContext> options = new();
        options.UseNpgsql("Host=localhost;Database=ledgerdb");
        LedgerDbContextOptions.ApplyConventions(options);
        return new LedgerDbContext(options.Options);
    }
}
