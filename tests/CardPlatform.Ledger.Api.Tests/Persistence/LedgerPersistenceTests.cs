using CardPlatform.Ledger.Api.Tests.Infrastructure;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Ledger.Api.Tests.Persistence;

[Collection(LedgerApiCollectionFixture.Name)]
public class LedgerPersistenceTests(LedgerApiFactory factory)
{
    // Fixed instant with a non-UTC offset: PostgreSQL stores microseconds, and the adapter stores instants in UTC.
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 15, 10, 30, 0, TimeSpan.FromHours(-3));

    [Fact]
    public async Task Saved_accounts_and_transaction_are_read_back_with_the_same_values()
    {
        Currency brl = Currency.Create("BRL").Value;
        Account settlement = Account.Open(AccountType.Settlement, brl, CreatedAt);
        Account card = Account.Open(AccountType.Card, brl, CreatedAt);
        Money amount = Money.Create(100.5m, brl).Value;
        LedgerTransaction topUp = LedgerTransaction.Create(
            [Entry.Debit(settlement.Id, amount), Entry.Credit(card.Id, amount)],
            CreatedAt,
            "Top-up").Value;
        Assert.True(topUp.ApplyTo([settlement, card]).IsSuccess);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            IAccountRepository accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            ILedgerTransactionRepository transactions = scope.ServiceProvider.GetRequiredService<ILedgerTransactionRepository>();
            accounts.Add(settlement);
            accounts.Add(card);
            transactions.Add(topUp);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            IAccountRepository accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            ILedgerTransactionRepository transactions = scope.ServiceProvider.GetRequiredService<ILedgerTransactionRepository>();

            IReadOnlyList<Account> loadedAccounts = await accounts.GetByIdsAsync([settlement.Id, card.Id]);
            Assert.Equal(2, loadedAccounts.Count);
            AssertSameAccount(settlement, loadedAccounts.Single(account => account.Id == settlement.Id));
            AssertSameAccount(card, (await accounts.GetByIdAsync(card.Id))!);

            LedgerTransaction? loadedTopUp = await transactions.GetByIdAsync(topUp.Id);
            Assert.NotNull(loadedTopUp);
            Assert.Equal(topUp.Id, loadedTopUp.Id);
            Assert.Equal("Top-up", loadedTopUp.Description);
            Assert.Equal(CreatedAt, loadedTopUp.CreatedAt);
            Assert.Equal(brl, loadedTopUp.Currency);
            Assert.Equal(
                topUp.Entries.OrderBy(entry => entry.Direction),
                loadedTopUp.Entries.OrderBy(entry => entry.Direction));
        }
    }

    [Fact]
    public async Task Unknown_ids_are_not_found()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        Guid unknownId = Guid.CreateVersion7();

        Assert.Null(await scope.ServiceProvider.GetRequiredService<IAccountRepository>().GetByIdAsync(unknownId));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IAccountRepository>().GetByIdsAsync([unknownId]));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ILedgerTransactionRepository>().GetByIdAsync(unknownId));
    }

    private static void AssertSameAccount(Account expected, Account actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Currency, actual.Currency);
        Assert.Equal(expected.Balance, actual.Balance);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
    }
}
