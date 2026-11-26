using CardPlatform.Settlement.Job.Seeding;

namespace CardPlatform.Settlement.Job.Tests.Seeding;

public sealed class PurchaseGeneratorTests
{
    private static readonly SeedOptions Options = new(5_000, 30, 200, new DateOnly(2026, 11, 20), "BRL", 11);

    [Fact]
    public void The_same_options_generate_the_same_purchases()
    {
        Assert.Equal(new PurchaseGenerator(Options).Generate(), new PurchaseGenerator(Options).Generate());
    }

    [Fact]
    public void Another_seed_generates_other_purchases()
    {
        Assert.NotEqual(
            new PurchaseGenerator(Options).Generate(),
            new PurchaseGenerator(Options with { Seed = 12 }).Generate());
    }

    [Fact]
    public void Purchases_stay_within_the_day_the_accounts_and_the_amount_range()
    {
        PurchaseGenerator generator = new(Options);
        DateTimeOffset dayStart = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

        List<SeededPurchase> purchases = [.. generator.Generate()];

        Assert.Equal(Options.Transactions, purchases.Count);
        Assert.All(purchases, purchase =>
        {
            Assert.InRange(purchase.CreatedAt, dayStart, dayStart.AddDays(1).AddTicks(-1));
            Assert.Equal(TimeSpan.Zero, purchase.CreatedAt.Offset);
            Assert.Equal(0, purchase.CreatedAt.Ticks % TimeSpan.TicksPerMicrosecond);
            Assert.InRange(purchase.Card, 0, Options.Cards - 1);
            Assert.InRange(purchase.Merchant, 0, Options.Merchants - 1);
            Assert.InRange(
                generator.ToAmount(purchase.Units),
                PurchaseGenerator.MinimumAmount,
                PurchaseGenerator.MaximumAmount);
        });
    }

    [Theory]
    [InlineData("BRL", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KWD", 2)]
    public void Amounts_have_the_currency_minor_units_but_never_more_than_the_ledger_stores(string currency, int decimals)
    {
        PurchaseGenerator generator = new(Options with { Currency = currency });

        Assert.Equal(decimals, generator.DecimalPlaces);
        Assert.All(generator.Generate(), purchase =>
        {
            decimal amount = generator.ToAmount(purchase.Units);
            Assert.Equal(amount, decimal.Round(amount, decimals));
        });
    }
}
