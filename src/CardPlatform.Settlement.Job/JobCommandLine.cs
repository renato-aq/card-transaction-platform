using CardPlatform.Settlement.Job.Generation;
using CardPlatform.Settlement.Job.Seeding;

namespace CardPlatform.Settlement.Job;

/// <summary>What the job process was asked to do.</summary>
public enum JobCommand
{
    /// <summary>
    /// Writes the settlement file (<see cref="RunOptions"/>) and exits. The default: the Aspire dashboard starts the job
    /// with no arguments.
    /// </summary>
    Run,

    /// <summary>Loads generated purchases into the ledger (<see cref="SeedOptions"/>) and exits.</summary>
    Seed,
}

/// <summary>
/// The parsed command line: <c>[command] [options]</c>. With no arguments, or when the first one is an option, the
/// command is <see cref="JobCommand.Run"/>, the default: its options (<c>--date</c>, <c>--currency</c>,
/// <c>--output</c>) are read exactly as after <c>run</c>, and every other argument (such as
/// <c>--environment=Development</c>) goes to the host configuration as before. Otherwise the first argument names the
/// command and the rest are its options.
/// </summary>
/// <param name="Command">The command to run.</param>
/// <param name="HostArgs">
/// Arguments for the host configuration (only for <see cref="JobCommand.Run"/> started without the command name).
/// </param>
/// <param name="Seed">Options of <see cref="JobCommand.Seed"/>; <see langword="null"/> for other commands.</param>
/// <param name="Run">Options of <see cref="JobCommand.Run"/>; <see langword="null"/> for other commands.</param>
public sealed record JobCommandLine(JobCommand Command, IReadOnlyList<string> HostArgs, SeedOptions? Seed, RunOptions? Run = null)
{
    public const string RunCommandName = "run";

    public const string SeedCommandName = "seed";

    /// <summary>Usage text of the command line, shown when it is invalid.</summary>
    public static readonly string Usage =
        $"""
        Usage: CardPlatform.Settlement.Job [command] [options]
        Commands:
          {RunCommandName}     write the settlement file of a date, then exit (the default, with no arguments)
          {SeedCommandName}    populate the ledger with generated purchases, then exit

        {RunOptions.Usage}

        {SeedOptions.Usage}
        """;

    /// <summary>
    /// Parses <paramref name="args"/>; <paramref name="defaultDate"/> is the default settlement date and the default day
    /// of a seed.
    /// </summary>
    /// <returns>The command line, or <see langword="null"/> with <paramref name="error"/> describing what is wrong.</returns>
    public static JobCommandLine? Parse(IReadOnlyList<string> args, DateOnly defaultDate, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || args[0].StartsWith('-'))
        {
            List<string> runArgs = [];
            List<string> hostArgs = [];
            SplitRunOptions(args, runArgs, hostArgs);

            RunOptions? options = RunOptions.Parse(runArgs, defaultDate, out error);
            return options is null ? null : new JobCommandLine(JobCommand.Run, hostArgs, Seed: null, options);
        }

        if (args[0] == RunCommandName)
        {
            RunOptions? options = RunOptions.Parse([.. args.Skip(1)], defaultDate, out error);
            return options is null ? null : new JobCommandLine(JobCommand.Run, [], Seed: null, options);
        }

        if (args[0] == SeedCommandName)
        {
            SeedOptions? options = SeedOptions.Parse([.. args.Skip(1)], defaultDate, out error);
            return options is null ? null : new JobCommandLine(JobCommand.Seed, [], options);
        }

        error = $"Unknown command '{args[0]}'.";
        return null;
    }

    // Separates the run options, each with its value when given as "--name value", from the arguments of the host,
    // keeping the order of both. The run options are then validated as after "run".
    private static void SplitRunOptions(IReadOnlyList<string> args, List<string> runArgs, List<string> hostArgs)
    {
        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            if (!RunOptions.IsOption(arg))
            {
                hostArgs.Add(arg);
                continue;
            }

            runArgs.Add(arg);
            if (!arg.Contains('=', StringComparison.Ordinal) && index + 1 < args.Count)
            {
                runArgs.Add(args[++index]);
            }
        }
    }
}
