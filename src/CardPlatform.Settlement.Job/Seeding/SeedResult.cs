namespace CardPlatform.Settlement.Job.Seeding;

/// <summary>What a seed wrote to the ledger.</summary>
/// <param name="Purchases">Purchase transactions, all within the requested day.</param>
/// <param name="TopUps">Card top-up transactions that fund the purchases, dated the day before.</param>
/// <param name="Accounts">Accounts created: the settlement account, the merchants and the cards.</param>
/// <param name="Elapsed">How long the load took.</param>
public sealed record SeedResult(long Purchases, long TopUps, long Accounts, TimeSpan Elapsed);
