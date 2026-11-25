using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Transactions;
using CardPlatform.Ledger.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace CardPlatform.Ledger.Infrastructure.Persistence;

/// <summary>
/// EF Core context of the Ledger. Mappings live in <see cref="Configurations"/>; the domain model has no EF attributes.
/// </summary>
public sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<LedgerTransaction> Transactions => Set<LedgerTransaction>();

    public DbSet<Hold> Holds => Set<Hold>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // An account's or hold's xmin changed since it was read (ADR 0006). EF Core rolled the transaction back;
            // the Application layer only sees its own exception type.
            throw new ConcurrencyConflictException(
                "An account or hold was changed by another request since it was read; nothing was saved.",
                exception);
        }
        catch (DbUpdateException exception) when (IsConcurrentInsert(exception))
        {
            // Another request inserted the same hold or the holds account of the same currency first. It is a
            // concurrency conflict like the one above: a retry reloads the data and finds the row that won.
            throw new ConcurrencyConflictException(
                "A hold or holds account was created by another request at the same time; nothing was saved.",
                exception);
        }
    }

    void IUnitOfWork.DiscardChanges()
    {
        ChangeTracker.Clear();
    }

    // Unique violations of the rows that concurrent requests may both try to create. Other unique violations are
    // unexpected and keep propagating as they are.
    private static bool IsConcurrentInsert(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: HoldConfiguration.PrimaryKeyName or AccountConfiguration.HoldsAccountPerCurrencyIndex,
        };
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerDbContext).Assembly);
    }
}
