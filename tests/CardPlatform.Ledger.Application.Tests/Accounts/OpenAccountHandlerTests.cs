using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Accounts;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;

namespace CardPlatform.Ledger.Application.Tests.Accounts;

public class OpenAccountHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeAccountRepository _accounts = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly OpenAccountHandler _handler;

    public OpenAccountHandlerTests()
    {
        _handler = new OpenAccountHandler(_accounts, _unitOfWork, new FixedTimeProvider(Now));
    }

    [Theory]
    [InlineData("card", AccountType.Card)]
    [InlineData("Merchant", AccountType.Merchant)]
    [InlineData("SETTLEMENT", AccountType.Settlement)]
    public async Task Valid_command_opens_and_saves_an_account_with_zero_balance(string type, AccountType expectedType)
    {
        Result<AccountDto> result = await _handler.HandleAsync(new OpenAccount(type, "BRL"));

        Assert.True(result.IsSuccess);
        Account saved = Assert.Single(_accounts.Added);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(saved.Id, result.Value.Id);
        Assert.Equal(expectedType, result.Value.Type);
        Assert.Equal("BRL", result.Value.Currency);
        Assert.Equal(0m, result.Value.Balance.Amount);
        Assert.Equal("BRL", result.Value.Balance.Currency);
        Assert.Equal(Now, saved.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("savings")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("Card,Merchant")]
    public async Task Invalid_account_type_is_rejected_without_saving(string? type)
    {
        Result<AccountDto> result = await _handler.HandleAsync(new OpenAccount(type, "BRL"));

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.InvalidAccountTypeCode, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_accounts.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("brl")]
    [InlineData("REAL")]
    public async Task Invalid_currency_is_rejected_without_saving(string? currency)
    {
        Result<AccountDto> result = await _handler.HandleAsync(new OpenAccount("card", currency));

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_currency", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_accounts.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private sealed class FakeAccountRepository : IAccountRepository
    {
        public List<Account> Added { get; } = [];

        public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Added.Find(account => account.Id == id));
        }

        public Task<IReadOnlyList<Account>> GetByIdsAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<Account>>(Added.FindAll(account => ids.Contains(account.Id)));
        }

        public void Add(Account account)
        {
            Added.Add(account);
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }
    }
}
