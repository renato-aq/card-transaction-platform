using CardPlatform.Settlement.Job.FileLayout;

namespace CardPlatform.Settlement.Job.Seeding;

/// <summary>
/// Generates the purchases of a seed from a seeded pseudo-random generator, so the same <see cref="SeedOptions"/> always
/// give the same cards, merchants, amounts and instants. Amounts are kept as whole units of the smallest step the ledger
/// stores for the currency: its minor unit (cents for BRL), but never finer than the two decimal places of the ledger
/// amount columns (<c>numeric(18,2)</c>).
/// </summary>
public sealed class PurchaseGenerator
{
    /// <summary>Smallest purchase, in major units of the currency.</summary>
    public const int MinimumAmount = 1;

    /// <summary>Largest purchase, in major units of the currency.</summary>
    public const int MaximumAmount = 500;

    /// <summary>Decimal places of the ledger amount columns.</summary>
    public const int LedgerDecimalPlaces = 2;

    private const long MicrosecondsPerDay = TimeSpan.TicksPerDay / TimeSpan.TicksPerMicrosecond;

    private readonly SeedOptions _options;

    private readonly long _minimumUnits;

    private readonly long _maximumUnits;

    public PurchaseGenerator(SeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        DecimalPlaces = Math.Min(CurrencyMinorUnits.GetExponent(options.Currency), LedgerDecimalPlaces);
        UnitsPerMajorUnit = DecimalPlaces switch
        {
            0 => 1,
            1 => 10,
            _ => 100,
        };
        _minimumUnits = MinimumAmount * UnitsPerMajorUnit;
        _maximumUnits = MaximumAmount * UnitsPerMajorUnit;
        DayStart = new DateTimeOffset(options.Date, TimeOnly.MinValue, TimeSpan.Zero);
    }

    /// <summary>Decimal places of the generated amounts.</summary>
    public int DecimalPlaces { get; }

    /// <summary>How many generated units make one major unit of the currency.</summary>
    public long UnitsPerMajorUnit { get; }

    /// <summary>Start of the UTC day of the purchases.</summary>
    public DateTimeOffset DayStart { get; }

    /// <summary>Converts generated units to an amount in major units of the currency.</summary>
    public decimal ToAmount(long units)
    {
        return (decimal)units / UnitsPerMajorUnit;
    }

    /// <summary>
    /// The purchases, in generation order (not sorted by instant). Every enumeration starts again from the seed and
    /// returns the same sequence.
    /// </summary>
    public IEnumerable<SeededPurchase> Generate()
    {
        Random random = new(_options.Seed);

        for (int index = 0; index < _options.Transactions; index++)
        {
            int card = random.Next(_options.Cards);
            int merchant = random.Next(_options.Merchants);
            long units = random.NextInt64(_minimumUnits, _maximumUnits + 1);
            long microsecond = random.NextInt64(MicrosecondsPerDay);

            yield return new SeededPurchase(
                card,
                merchant,
                units,
                DayStart.AddTicks(microsecond * TimeSpan.TicksPerMicrosecond));
        }
    }
}
