using CardPlatform.Ledger.Api.Tests.Infrastructure;
using CardPlatform.Ledger.Application.Abstractions.Persistence;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Ledger.Api.Tests.Persistence;

[Collection(LedgerApiCollectionFixture.Name)]
public class HoldPersistenceTests(LedgerApiFactory factory)
{
    // Fixed instants with a non-UTC offset: the adapter stores instants in UTC.
    private static readonly DateTimeOffset CreatedAt = new(2026, 1, 15, 10, 30, 0, TimeSpan.FromHours(-3));

    private static readonly DateTimeOffset CapturedAt = CreatedAt.AddMinutes(5);

    private static readonly Currency Brl = Currency.Create("BRL").Value;

    [Fact]
    public async Task Saved_hold_is_read_back_with_the_same_values_and_state()
    {
        Account settlement = Account.Open(AccountType.Settlement, Brl, CreatedAt);
        Account card = Account.Open(AccountType.Card, Brl, CreatedAt);
        Account holds = Account.Open(AccountType.Holds, Brl, CreatedAt);
        Account merchant = Account.Open(AccountType.Merchant, Brl, CreatedAt);
        LedgerTransaction topUp = LedgerTransaction.Create(
            [Entry.Debit(settlement.Id, Reais(100m)), Entry.Credit(card.Id, Reais(100m))],
            CreatedAt).Value;
        Assert.True(topUp.ApplyTo([settlement, card]).IsSuccess);
        HoldPlacement placement = Hold.Place(Guid.CreateVersion7(), card, holds, Reais(30.25m), CreatedAt).Value;
        Hold hold = placement.Hold;

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            IAccountRepository accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            ILedgerTransactionRepository transactions = scope.ServiceProvider.GetRequiredService<ILedgerTransactionRepository>();
            accounts.Add(settlement);
            accounts.Add(card);
            accounts.Add(holds);
            accounts.Add(merchant);
            transactions.Add(topUp);
            transactions.Add(placement.Transaction);
            scope.ServiceProvider.GetRequiredService<IHoldRepository>().Add(hold);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            Hold? loaded = await scope.ServiceProvider.GetRequiredService<IHoldRepository>().GetByIdAsync(hold.Id);

            Assert.NotNull(loaded);
            AssertSameHold(hold, loaded);
            Assert.Equal(HoldStatus.Pending, loaded.Status);
        }

        // Capturing a loaded hold updates its row (checked against xmin) together with the accounts.
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            IAccountRepository accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            Hold loaded = (await scope.ServiceProvider.GetRequiredService<IHoldRepository>().GetByIdAsync(hold.Id))!;
            Account loadedHolds = (await accounts.GetByIdAsync(holds.Id))!;
            Account loadedMerchant = (await accounts.GetByIdAsync(merchant.Id))!;

            LedgerTransaction capture = loaded.Capture(loadedHolds, loadedMerchant, CapturedAt).Value;
            scope.ServiceProvider.GetRequiredService<ILedgerTransactionRepository>().Add(capture);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            hold = loaded;
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            IAccountRepository accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            Hold? loaded = await scope.ServiceProvider.GetRequiredService<IHoldRepository>().GetByIdAsync(hold.Id);

            Assert.NotNull(loaded);
            AssertSameHold(hold, loaded);
            Assert.Equal(HoldStatus.Captured, loaded.Status);
            Assert.Equal(CapturedAt, loaded.CompletedAt);
            Assert.NotNull(loaded.CompletionTransactionId);
            Assert.NotNull(await scope.ServiceProvider.GetRequiredService<ILedgerTransactionRepository>()
                .GetByIdAsync(loaded.CompletionTransactionId.Value));
            Assert.Equal(Reais(69.75m), (await accounts.GetByIdAsync(card.Id))!.Balance);
            Assert.Equal(Money.Zero(Brl), (await accounts.GetByIdAsync(holds.Id))!.Balance);
            Assert.Equal(Reais(30.25m), (await accounts.GetByIdAsync(merchant.Id))!.Balance);
        }
    }

    [Fact]
    public async Task Unknown_hold_is_not_found()
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        Assert.Null(await scope.ServiceProvider.GetRequiredService<IHoldRepository>().GetByIdAsync(Guid.CreateVersion7()));
    }

    private static Money Reais(decimal amount)
    {
        return Money.Create(amount, Brl).Value;
    }

    private static void AssertSameHold(Hold expected, Hold actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.CardAccountId, actual.CardAccountId);
        Assert.Equal(expected.Amount, actual.Amount);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.CompletedAt, actual.CompletedAt);
        Assert.Equal(expected.PlacementTransactionId, actual.PlacementTransactionId);
        Assert.Equal(expected.CompletionTransactionId, actual.CompletionTransactionId);
    }
}
