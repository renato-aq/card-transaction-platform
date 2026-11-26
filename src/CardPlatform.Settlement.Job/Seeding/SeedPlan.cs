namespace CardPlatform.Settlement.Job.Seeding;

/// <summary>An account the seed creates.</summary>
/// <param name="Id">UUID v7 derived from <paramref name="CreatedAt"/>, as the ledger opens accounts.</param>
/// <param name="CreatedAt">When the account was opened, in UTC and whole microseconds.</param>
internal readonly record struct PlannedAccount(Guid Id, DateTimeOffset CreatedAt);

/// <summary>
/// The accounts of a seed and the totals that give their final balances, computed by a first pass over the
/// <see cref="PurchaseGenerator"/> before anything is written. Accounts are opened at the start of the day before the
/// purchases, one microsecond apart in a stable order (the settlement account, the merchants, then the cards), so the
/// same options always open the n-th merchant at the same instant.
/// </summary>
internal sealed class SeedPlan
{
    private SeedPlan(
        PlannedAccount settlementAccount,
        PlannedAccount[] merchantAccounts,
        PlannedAccount[] cardAccounts,
        long[] merchantUnits,
        long[] cardUnits,
        long[] topUpUnits)
    {
        SettlementAccount = settlementAccount;
        MerchantAccounts = merchantAccounts;
        CardAccounts = cardAccounts;
        MerchantUnits = merchantUnits;
        CardUnits = cardUnits;
        TopUpUnits = topUpUnits;
        TotalTopUpUnits = topUpUnits.Sum();
    }

    public PlannedAccount SettlementAccount { get; }

    public PlannedAccount[] MerchantAccounts { get; }

    public PlannedAccount[] CardAccounts { get; }

    /// <summary>Total credited to each merchant by its purchases, in generated units.</summary>
    public long[] MerchantUnits { get; }

    /// <summary>Total debited from each card by its purchases, in generated units.</summary>
    public long[] CardUnits { get; }

    /// <summary>Top-up of each card, in generated units; zero for a card without purchases, which gets no top-up.</summary>
    public long[] TopUpUnits { get; }

    public long TotalTopUpUnits { get; }

    public long AccountCount => 1L + MerchantAccounts.Length + CardAccounts.Length;

    public static SeedPlan Create(PurchaseGenerator generator, SeedOptions options)
    {
        long[] merchantUnits = new long[options.Merchants];
        long[] cardUnits = new long[options.Cards];
        foreach (SeededPurchase purchase in generator.Generate())
        {
            merchantUnits[purchase.Merchant] += purchase.Units;
            cardUnits[purchase.Card] += purchase.Units;
        }

        long step = LedgerSeeder.TopUpStep * generator.UnitsPerMajorUnit;
        long[] topUpUnits = new long[options.Cards];
        for (int card = 0; card < cardUnits.Length; card++)
        {
            topUpUnits[card] = (cardUnits[card] + step - 1) / step * step;
        }

        DateTimeOffset openedAt = generator.DayStart.AddDays(-1);
        long sequence = 0;
        PlannedAccount settlementAccount = Open(openedAt, sequence++);
        PlannedAccount[] merchantAccounts = new PlannedAccount[options.Merchants];
        for (int merchant = 0; merchant < merchantAccounts.Length; merchant++)
        {
            merchantAccounts[merchant] = Open(openedAt, sequence++);
        }

        PlannedAccount[] cardAccounts = new PlannedAccount[options.Cards];
        for (int card = 0; card < cardAccounts.Length; card++)
        {
            cardAccounts[card] = Open(openedAt, sequence++);
        }

        return new SeedPlan(settlementAccount, merchantAccounts, cardAccounts, merchantUnits, cardUnits, topUpUnits);
    }

    private static PlannedAccount Open(DateTimeOffset openedAt, long sequence)
    {
        DateTimeOffset createdAt = openedAt.AddTicks(sequence * TimeSpan.TicksPerMicrosecond);
        return new PlannedAccount(Guid.CreateVersion7(createdAt), createdAt);
    }
}
