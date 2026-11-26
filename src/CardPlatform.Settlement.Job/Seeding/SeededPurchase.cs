namespace CardPlatform.Settlement.Job.Seeding;

/// <summary>
/// A generated purchase: <paramref name="Card"/> pays <paramref name="Merchant"/> at <paramref name="CreatedAt"/>.
/// </summary>
/// <param name="Card">Index of the card account, from 0.</param>
/// <param name="Merchant">Index of the merchant account, from 0.</param>
/// <param name="Units">Amount in the smallest unit the ledger stores for the currency (see <see cref="PurchaseGenerator"/>).</param>
/// <param name="CreatedAt">When the purchase happened, in UTC and whole microseconds.</param>
public readonly record struct SeededPurchase(int Card, int Merchant, long Units, DateTimeOffset CreatedAt);
