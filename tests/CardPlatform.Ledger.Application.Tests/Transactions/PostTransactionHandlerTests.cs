using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Application.Transactions;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Tests.Transactions;

public class PostTransactionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Currency Brl = Currency.Create("BRL").Value;

    private readonly Account _settlement = Account.Open(AccountType.Settlement, Brl, Now);
    private readonly Account _card = Account.Open(AccountType.Card, Brl, Now);
    private readonly Account _merchant = Account.Open(AccountType.Merchant, Brl, Now);
    private readonly Account _usdMerchant = Account.Open(AccountType.Merchant, Currency.Create("USD").Value, Now);
    private readonly FakeAccountRepository _accounts;
    private readonly FakeLedgerTransactionRepository _transactions = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly PostTransactionHandler _handler;

    public PostTransactionHandlerTests()
    {
        _accounts = new FakeAccountRepository(_settlement, _card, _merchant, _usdMerchant);
        _handler = new PostTransactionHandler(_accounts, _transactions, _unitOfWork, new FixedTimeProvider(Now));
    }

    [Fact]
    public async Task Valid_command_applies_and_saves_the_transaction_once()
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            "  Top-up  ",
            "BRL",
            [new(_settlement.Id, "debit", 100m), new(_card.Id, "Credit", 100m)]));

        Assert.True(result.IsSuccess);
        LedgerTransaction saved = Assert.Single(_transactions.Added);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(saved.Id, result.Value.Id);
        Assert.Equal("Top-up", result.Value.Description);
        Assert.Equal("BRL", result.Value.Currency);
        Assert.Equal(Now, result.Value.CreatedAt);
        Assert.Equal(
            [new EntryDto(_settlement.Id, EntryDirection.Debit, 100m), new EntryDto(_card.Id, EntryDirection.Credit, 100m)],
            result.Value.Entries);
        Assert.Equal(-100m, _settlement.Balance.Amount);
        Assert.Equal(100m, _card.Balance.Amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("brl")]
    [InlineData("REAL")]
    public async Task Invalid_currency_is_rejected_before_loading_accounts(string? currency)
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            null,
            currency,
            [new(_settlement.Id, "debit", 10m), new(_card.Id, "credit", 10m)]));

        AssertRejected(result, Currency.InvalidCurrencyCode, ErrorType.Validation);
        Assert.Equal(0, _accounts.LoadCount);
    }

    [Fact]
    public async Task Null_entry_is_rejected()
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            null,
            "BRL",
            [new(_settlement.Id, "debit", 10m), null]));

        AssertRejected(result, TransactionErrors.InvalidEntryCode, ErrorType.Validation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Missing_or_empty_account_id_is_rejected(string? accountId)
    {
        Guid? id = accountId is null ? null : Guid.Parse(accountId);

        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            null,
            "BRL",
            [new(_settlement.Id, "debit", 10m), new(id, "credit", 10m)]));

        AssertRejected(result, TransactionErrors.InvalidAccountIdCode, ErrorType.Validation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("refund")]
    [InlineData("0")]
    [InlineData("1")]
    public async Task Invalid_direction_is_rejected(string? direction)
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            null,
            "BRL",
            [new(_settlement.Id, "debit", 10m), new(_card.Id, direction, 10m)]));

        AssertRejected(result, TransactionErrors.InvalidDirectionCode, ErrorType.Validation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Missing_or_non_positive_amount_is_rejected(int? amount)
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            null,
            "BRL",
            [new(_settlement.Id, "debit", amount), new(_card.Id, "credit", amount)]));

        AssertRejected(result, LedgerTransaction.InvalidAmountCode, ErrorType.Validation);
        Assert.Equal(0, _accounts.LoadCount);
    }

    [Fact]
    public async Task Amount_with_more_than_two_decimal_places_is_rejected()
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            null,
            "BRL",
            [new(_settlement.Id, "debit", 10.001m), new(_card.Id, "credit", 10.001m)]));

        AssertRejected(result, Money.TooManyDecimalPlacesCode, ErrorType.Validation);
    }

    [Fact]
    public async Task Null_entries_are_rejected_as_insufficient_entries()
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(null, "BRL", null));

        AssertRejected(result, LedgerTransaction.InsufficientEntriesCode, ErrorType.Validation);
    }

    [Fact]
    public async Task Single_entry_is_rejected_as_insufficient_entries()
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            null,
            "BRL",
            [new(_card.Id, "credit", 10m)]));

        AssertRejected(result, LedgerTransaction.InsufficientEntriesCode, ErrorType.Validation);
    }

    [Fact]
    public async Task Unbalanced_transaction_is_rejected_before_loading_accounts()
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            null,
            "BRL",
            [new(_settlement.Id, "debit", 10m), new(_card.Id, "credit", 9.99m)]));

        AssertRejected(result, LedgerTransaction.UnbalancedTransactionCode, ErrorType.BusinessRule);
        Assert.Equal(0, _accounts.LoadCount);
    }

    [Fact]
    public async Task Unknown_account_is_rejected_as_not_found()
    {
        Guid unknownId = Guid.CreateVersion7();

        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            null,
            "BRL",
            [new(_settlement.Id, "debit", 10m), new(unknownId, "credit", 10m)]));

        AssertRejected(result, "account_not_found", ErrorType.NotFound);
        Assert.Contains(unknownId.ToString(), result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(0m, _settlement.Balance.Amount);
    }

    [Fact]
    public async Task Account_in_another_currency_is_rejected_as_currency_mismatch()
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            null,
            "BRL",
            [new(_settlement.Id, "debit", 10m), new(_usdMerchant.Id, "credit", 10m)]));

        AssertRejected(result, LedgerTransaction.CurrencyMismatchCode, ErrorType.BusinessRule);
        Assert.Equal(0m, _settlement.Balance.Amount);
    }

    [Fact]
    public async Task Purchase_above_card_balance_is_rejected_as_insufficient_funds_without_changing_balances()
    {
        Result<LedgerTransactionDto> result = await _handler.HandleAsync(new PostTransaction(
            "Purchase",
            "BRL",
            [new(_card.Id, "debit", 0.01m), new(_merchant.Id, "credit", 0.01m)]));

        AssertRejected(result, LedgerTransaction.InsufficientFundsCode, ErrorType.BusinessRule);
        Assert.Equal(0m, _card.Balance.Amount);
        Assert.Equal(0m, _merchant.Balance.Amount);
    }

    private void AssertRejected(Result<LedgerTransactionDto> result, string expectedCode, ErrorType expectedType)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(expectedType, result.Error.Type);
        Assert.Empty(_transactions.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private sealed class FakeAccountRepository(params Account[] existing) : IAccountRepository
    {
        public int LoadCount { get; private set; }

        public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(Array.Find(existing, account => account.Id == id));
        }

        public Task<IReadOnlyList<Account>> GetByIdsAsync(
            IReadOnlyCollection<Guid> ids,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult<IReadOnlyList<Account>>(Array.FindAll(existing, account => ids.Contains(account.Id)));
        }

        public Task<Account?> GetHoldsAccountAsync(Currency currency, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Posting a transaction must not look up holds accounts.");
        }

        public void Add(Account account)
        {
            throw new InvalidOperationException("Posting a transaction must not add accounts.");
        }
    }

    private sealed class FakeLedgerTransactionRepository : ILedgerTransactionRepository
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
