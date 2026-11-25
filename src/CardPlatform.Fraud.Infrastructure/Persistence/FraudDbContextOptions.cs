using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Fraud.Infrastructure.Persistence;

/// <summary>
/// Options shared by the runtime registration and the design-time factory, so migrations are generated
/// with the same conventions the application uses.
/// </summary>
internal static class FraudDbContextOptions
{
    public static DbContextOptionsBuilder ApplyConventions(DbContextOptionsBuilder options)
    {
        return options.UseSnakeCaseNamingConvention();
    }
}
