using System.Globalization;

namespace CardPlatform.Settlement.Job.Seeding;

/// <summary>
/// Parameters of the <c>seed</c> command, which loads purchase transactions into the ledger for the settlement job to
/// measure against. The same options, seed included, always generate the same amounts, merchants and instants.
/// </summary>
/// <param name="Transactions">Number of purchase transactions to generate.</param>
/// <param name="Merchants">Number of merchant accounts the purchases are spread over.</param>
/// <param name="Cards">Number of card accounts the purchases are spread over.</param>
/// <param name="Date">UTC day in which every purchase happens.</param>
/// <param name="Currency">ISO 4217 code of every account and amount.</param>
/// <param name="Seed">Seed of the pseudo-random generator.</param>
public sealed record SeedOptions(int Transactions, int Merchants, int Cards, DateOnly Date, string Currency, int Seed)
{
    public const int DefaultTransactions = 5_000_000;

    public const int DefaultMerchants = 1_000;

    public const int DefaultCards = 100_000;

    public const string DefaultCurrency = "BRL";

    public const int DefaultSeed = 42;

    /// <summary>Usage text of the options, shown when they are invalid.</summary>
    public const string Usage =
        """
        Usage: seed [options]
          --transactions <n>  purchase transactions to generate (default 5000000)
          --merchants <n>     merchant accounts to create (default 1000)
          --cards <n>         card accounts to create (default 100000)
          --date <yyyy-MM-dd> UTC day of the purchases (default yesterday, UTC)
          --currency <code>   ISO 4217 currency code (default BRL)
          --seed <n>          seed of the generator (default 42)
        """;

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Parses the options that follow <c>seed</c> on the command line, as <c>--name value</c> or <c>--name=value</c>.
    /// Options that are not given take their defaults; <paramref name="defaultDate"/> is the default day.
    /// </summary>
    /// <returns>The options, or <see langword="null"/> with <paramref name="error"/> describing what is wrong.</returns>
    public static SeedOptions? Parse(IReadOnlyList<string> args, DateOnly defaultDate, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        SeedOptions options = new(DefaultTransactions, DefaultMerchants, DefaultCards, defaultDate, DefaultCurrency, DefaultSeed);

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected argument '{arg}'.";
                return null;
            }

            string name;
            string? value;
            int separator = arg.IndexOf('=', StringComparison.Ordinal);
            if (separator >= 0)
            {
                name = arg[2..separator];
                value = arg[(separator + 1)..];
            }
            else
            {
                name = arg[2..];
                value = index + 1 < args.Count ? args[++index] : null;
            }

            if (value is null)
            {
                error = $"Option '--{name}' needs a value.";
                return null;
            }

            SeedOptions? updated = name switch
            {
                "transactions" => TryParsePositive(value) is int count ? options with { Transactions = count } : null,
                "merchants" => TryParsePositive(value) is int count ? options with { Merchants = count } : null,
                "cards" => TryParsePositive(value) is int count ? options with { Cards = count } : null,
                "date" => DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                    ? options with { Date = date }
                    : null,
                "currency" => IsCurrencyCode(value) ? options with { Currency = value } : null,
                "seed" => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int seed)
                    ? options with { Seed = seed }
                    : null,
                _ => null,
            };

            if (updated is null)
            {
                error = name is "transactions" or "merchants" or "cards" or "date" or "currency" or "seed"
                    ? $"Invalid value '{value}' for option '--{name}'."
                    : $"Unknown option '--{name}'.";
                return null;
            }

            options = updated;
        }

        error = null;
        return options;
    }

    private static int? TryParsePositive(string value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count > 0
            ? count
            : null;
    }

    // Same rule as the ledger: exactly three uppercase ASCII letters.
    private static bool IsCurrencyCode(string value)
    {
        return value.Length == 3 && value.All(char.IsAsciiLetterUpper);
    }
}
