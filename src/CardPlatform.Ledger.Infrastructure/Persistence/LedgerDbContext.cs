using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Transactions;

using Microsoft.EntityFrameworkCore;

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
    }

    void IUnitOfWork.DiscardChanges()
    {
        ChangeTracker.Clear();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LedgerDbContext).Assembly);
    }
}
