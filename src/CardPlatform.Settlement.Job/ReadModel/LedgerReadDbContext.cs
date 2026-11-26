using CardPlatform.Settlement.Job.Seeding;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Settlement.Job.ReadModel;

/// <summary>
/// The job's own read-only EF Core model of the ledger tables (<c>accounts</c>, <c>transactions</c>, <c>entries</c>).
/// The schema is owned by the Ledger, which creates it with its migrations; this model maps only what the settlement
/// needs, has no migrations and never saves: <see cref="SaveChanges(bool)"/> and
/// <see cref="SaveChangesAsync(bool, CancellationToken)"/> throw. The job couples to the table and column names and to
/// the stored values (enums as their names, amounts as <c>numeric(18,2)</c>, currencies as <c>char(3)</c>), not to
/// the Ledger's projects.
/// </summary>
public sealed class LedgerReadDbContext(DbContextOptions<LedgerReadDbContext> options) : DbContext(options)
{
    private const string AmountColumnType = "numeric(18,2)";

    private const string CurrencyColumnType = "char(3)";

    private const int EnumMaxLength = 20;

    public DbSet<AccountRecord> Accounts => Set<AccountRecord>();

    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();

    public DbSet<EntryRecord> Entries => Set<EntryRecord>();

    /// <summary>Always throws: the job never writes to the ledger.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        throw ReadOnly();
    }

    /// <summary>Always throws: the job never writes to the ledger.</summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        throw ReadOnly();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AccountRecord>(account =>
        {
            account.ToTable(LedgerSchema.Accounts);
            account.HasKey(row => row.Id);
            account.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            account.Property(row => row.Type).HasColumnName("type").HasMaxLength(EnumMaxLength);
            account.Property(row => row.Currency).HasColumnName("currency").HasColumnType(CurrencyColumnType);
        });

        modelBuilder.Entity<TransactionRecord>(transaction =>
        {
            transaction.ToTable(LedgerSchema.Transactions);
            transaction.HasKey(row => row.Id);
            transaction.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            transaction.Property(row => row.Description).HasColumnName("description");
            transaction.Property(row => row.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<EntryRecord>(entry =>
        {
            entry.ToTable(LedgerSchema.Entries);
            entry.HasKey(row => row.Id);
            entry.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            entry.Property(row => row.TransactionId).HasColumnName("transaction_id");
            entry.Property(row => row.AccountId).HasColumnName("account_id");
            entry.Property(row => row.Direction).HasColumnName("direction").HasMaxLength(EnumMaxLength);
            entry.Property(row => row.Amount).HasColumnName("amount").HasColumnType(AmountColumnType);
            entry.Property(row => row.Currency).HasColumnName("currency").HasColumnType(CurrencyColumnType);

            entry.HasOne(row => row.Transaction).WithMany().HasForeignKey(row => row.TransactionId);
            entry.HasOne(row => row.Account).WithMany().HasForeignKey(row => row.AccountId);
        });
    }

    private static NotSupportedException ReadOnly()
    {
        return new NotSupportedException("The settlement job reads the ledger and never writes to it.");
    }
}
