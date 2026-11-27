using System.Globalization;

using CardPlatform.Settlement.Job.FileLayout;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>Parameters of the <c>run</c> command, which writes the settlement file of one date and one currency.</summary>
/// <param name="Date">The settlement date: the UTC day whose transactions are settled.</param>
/// <param name="Currency">ISO 4217 code of the entries to settle.</param>
/// <param name="OutputDirectory">Directory of the file; a relative path is resolved against the working directory.</param>
/// <param name="MetricsOutputPath">
/// File to write the metrics of the run to, as JSON (<see cref="Measurement.RunMetrics"/>), or <see langword="null"/>
/// to only log them; a relative path is resolved against the working directory. <see cref="Parse"/> rejects the path of
/// the settlement file itself.
/// </param>
/// <param name="Strategy">
/// Name of the <see cref="ISettlementFileGenerator"/> that writes the file: <c>optimized</c>
/// (<see cref="OptimizedSettlementFileGenerator"/>, the default) or <c>naive</c> (<see cref="NaiveSettlementFileGenerator"/>,
/// the baseline). Both write the same file.
/// </param>
/// <param name="PageSize">
/// Records read per query by the <c>optimized</c> strategy (keyset pagination); the <c>naive</c> strategy reads the day in
/// one query and ignores it.
/// </param>
/// <param name="Parallelism">
/// Partitions the <c>optimized</c> strategy reads at the same time, each on a connection of its own; 1 reads everything in
/// one sequence on one connection. The <c>naive</c> strategy reads on one connection and ignores it.
/// </param>
public sealed record RunOptions(
    DateOnly Date,
    string Currency,
    string OutputDirectory,
    string? MetricsOutputPath = null,
    string Strategy = RunOptions.DefaultStrategy,
    int PageSize = RunOptions.DefaultPageSize,
    int Parallelism = RunOptions.DefaultParallelism)
{
    public const string DefaultCurrency = "BRL";

    /// <summary>The strategy of a run that does not choose one: the streaming version.</summary>
    public const string DefaultStrategy = OptimizedSettlementFileGenerator.StrategyName;

    /// <summary>
    /// The page size of a run that does not choose one. Measured on a seeded day of 500,000 records: larger pages gain
    /// little time, and each page costs one query and keeps up to this many records in flight in the database driver.
    /// </summary>
    public const int DefaultPageSize = 10_000;

    /// <summary>
    /// The parallelism of a run that does not choose one. Each partition holds a PostgreSQL connection for the whole run,
    /// so the default stays low next to the server's connection limit (100 by default), shared with the services; on a
    /// seeded day of 500,000 records, higher degrees gained little more time.
    /// </summary>
    public const int DefaultParallelism = 4;

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
          --metrics-output <file>
                              also write the metrics of the run to this JSON file (default: only logged)
          --strategy <name>   optimized (streaming, the default) or naive (version 1, the baseline)
          --page-size <n>     records per query of the optimized strategy (default 10000)
          --parallelism <n>   partitions the optimized strategy reads at the same time, one database connection each
                              (default 4; 1 reads sequentially)
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
                "metrics-output" => string.IsNullOrWhiteSpace(value) ? null : options with { MetricsOutputPath = value },
                "strategy" => IsStrategy(value) ? options with { Strategy = value } : null,
                "page-size" => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int pageSize) && pageSize > 0
                    ? options with { PageSize = pageSize }
                    : null,
                "parallelism" => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parallelism) && parallelism > 0
                    ? options with { Parallelism = parallelism }
                    : null,
                _ => null,
            };

            if (updated is null)
            {
                error = IsOptionName(name)
                    ? $"Invalid value '{value}' for option '--{name}'."
                    : $"Unknown option '--{name}'.";
                return null;
            }

            options = updated;
        }

        // Checked once every option is read: the settlement file's name depends on the date and the currency.
        if (options.MetricsOutputPath is not null && IsSettlementFilePath(options, options.MetricsOutputPath))
        {
            error = $"Invalid value '{options.MetricsOutputPath}' for option '--metrics-output': "
                + $"{Path.GetFullPath(options.MetricsOutputPath)} is the settlement file of this run; give another file.";
            return null;
        }

        error = null;
        return options;
    }

    /// <summary>
    /// Whether <paramref name="arg"/> is one of the options of <see cref="Parse"/> (<c>--date</c>, <c>--currency</c>,
    /// <c>--output</c>, <c>--metrics-output</c>, <c>--strategy</c>, <c>--page-size</c>, <c>--parallelism</c>), in either form:
    /// <c>--name</c> or <c>--name=value</c>.
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
        return IsOptionName(name);
    }

    private static bool IsOptionName(string name)
    {
        return name is "date" or "currency" or "output" or "metrics-output" or "strategy" or "page-size" or "parallelism";
    }

    /// <summary>
    /// Whether <paramref name="path"/> resolves to the settlement file the run writes
    /// (<see cref="SettlementFileWriter.FileName"/> in <see cref="OutputDirectory"/>), both resolved against the working
    /// directory as the run resolves them. Paths that differ only in case are the same file on Windows and macOS, whose
    /// file systems ignore case by default; on Linux they are different files.
    /// </summary>
    private static bool IsSettlementFilePath(RunOptions options, string path)
    {
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        try
        {
            string settlementFile = Path.GetFullPath(Path.Combine(
                Path.GetFullPath(options.OutputDirectory),
                SettlementFileWriter.FileName(new SettlementHeader(options.Date, options.Currency))));
            return string.Equals(Path.GetFullPath(path), settlementFile, comparison);
        }
        catch (ArgumentException)
        {
            // Paths that cannot be resolved are left to the run, which fails on them as before, with a logged error.
            return false;
        }
    }

    private static bool IsStrategy(string value)
    {
        return value is NaiveSettlementFileGenerator.StrategyName or OptimizedSettlementFileGenerator.StrategyName;
    }

    // Same rule as the ledger: exactly three uppercase ASCII letters.
    private static bool IsCurrencyCode(string value)
    {
        return value.Length == 3 && value.All(char.IsAsciiLetterUpper);
    }
}
