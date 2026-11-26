using System.Globalization;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>Parameters of the <c>run</c> command, which writes the settlement file of one date and one currency.</summary>
/// <param name="Date">The settlement date: the UTC day whose transactions are settled.</param>
/// <param name="Currency">ISO 4217 code of the entries to settle.</param>
/// <param name="OutputDirectory">Directory of the file; a relative path is resolved against the working directory.</param>
public sealed record RunOptions(DateOnly Date, string Currency, string OutputDirectory)
{
    public const string DefaultCurrency = "BRL";

    /// <summary>
    /// Relative to the working directory, which is the project directory when the AppHost starts the job, so the files
    /// land in <c>src/CardPlatform.Settlement.Job/settlement-files</c>, ignored by git.
    /// </summary>
    public const string DefaultOutputDirectory = "settlement-files";

    /// <summary>Usage text of the options, shown when they are invalid.</summary>
    public const string Usage =
        """
        Usage: [run] [options]
          --date <yyyy-MM-dd> settlement date, a UTC day (default yesterday, UTC)
          --currency <code>   ISO 4217 currency code (default BRL)
          --output <dir>      output directory (default settlement-files, under the working directory)
        """;

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>The options of a run without arguments: <paramref name="defaultDate"/>, BRL and the default directory.</summary>
    public static RunOptions Default(DateOnly defaultDate)
    {
        return new RunOptions(defaultDate, DefaultCurrency, DefaultOutputDirectory);
    }

    /// <summary>
    /// Parses the options that follow <c>run</c> on the command line, as <c>--name value</c> or <c>--name=value</c>.
    /// Options that are not given take their defaults; <paramref name="defaultDate"/> is the default settlement date.
    /// </summary>
    /// <returns>The options, or <see langword="null"/> with <paramref name="error"/> describing what is wrong.</returns>
    public static RunOptions? Parse(IReadOnlyList<string> args, DateOnly defaultDate, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        RunOptions options = Default(defaultDate);

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

            RunOptions? updated = name switch
            {
                "date" => DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                    ? options with { Date = date }
                    : null,
                "currency" => IsCurrencyCode(value) ? options with { Currency = value } : null,
                "output" => string.IsNullOrWhiteSpace(value) ? null : options with { OutputDirectory = value },
                _ => null,
            };

            if (updated is null)
            {
                error = name is "date" or "currency" or "output"
                    ? $"Invalid value '{value}' for option '--{name}'."
                    : $"Unknown option '--{name}'.";
                return null;
            }

            options = updated;
        }

        error = null;
        return options;
    }

    /// <summary>
    /// Whether <paramref name="arg"/> is one of the options of <see cref="Parse"/> (<c>--date</c>, <c>--currency</c>,
    /// <c>--output</c>), in either form: <c>--name</c> or <c>--name=value</c>.
    /// </summary>
    public static bool IsOption(string arg)
    {
        ArgumentNullException.ThrowIfNull(arg);

        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        int separator = arg.IndexOf('=', StringComparison.Ordinal);
        string name = separator >= 0 ? arg[2..separator] : arg[2..];
        return name is "date" or "currency" or "output";
    }

    // Same rule as the ledger: exactly three uppercase ASCII letters.
    private static bool IsCurrencyCode(string value)
    {
        return value.Length == 3 && value.All(char.IsAsciiLetterUpper);
    }
}
