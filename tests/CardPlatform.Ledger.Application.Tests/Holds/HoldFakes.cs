using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Tests.Holds;

/// <summary>In-memory adapters shared by the hold handler tests.</summary>
internal sealed class FakeAccountRepository(params Account[] existing) : IAccountRepository
{
    private readonly List<Account> _accounts = [.. existing];

    public List<Account> Added { get; } = [];

    public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_accounts.Find(account => account.Id == id));
    }

    public Task<IReadOnlyList<Account>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Account>>(_accounts.FindAll(account => ids.Contains(account.Id)));
    }

    public Task<Account?> GetHoldsAccountAsync(Currency currency, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_accounts.Find(account => account.Type == AccountType.Holds && account.Currency == currency));
    }

    public void Add(Account account)
    {
        Added.Add(account);
        _accounts.Add(account);
    }
}

internal sealed class FakeHoldRepository(params Hold[] existing) : IHoldRepository
{
    private readonly List<Hold> _holds = [.. existing];

    public List<Hold> Added { get; } = [];

    public Task<Hold?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_holds.Find(hold => hold.Id == id));
    }

    public void Add(Hold hold)
    {
        Added.Add(hold);
        _holds.Add(hold);
    }
}

internal sealed class FakeLedgerTransactionRepository : ILedgerTransactionRepository
{
    public List<LedgerTransaction> Added { get; } = [];

    public Task<LedgerTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Added.Find(transaction => transaction.Id == id));
    }

    public void Add(LedgerTransaction transaction)
    {
        Added.Add(transaction);
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public void DiscardChanges()
    {
        throw new InvalidOperationException("Handlers must not discard changes; only the retry decorator does.");
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }
}
