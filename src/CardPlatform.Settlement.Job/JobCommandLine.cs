using CardPlatform.Settlement.Job.Seeding;

namespace CardPlatform.Settlement.Job;

/// <summary>What the job process was asked to do.</summary>
public enum JobCommand
{
    /// <summary>Runs the settlement job. The default: the Aspire dashboard starts the job with no arguments.</summary>
    Run,

    /// <summary>Loads generated purchases into the ledger (<see cref="SeedOptions"/>) and exits.</summary>
    Seed,
}

/// <summary>
/// The parsed command line: <c>[command] [options]</c>. With no arguments, or when the first one is an option (such as
/// <c>--environment=Development</c>), the job runs and every argument goes to the host configuration as before.
/// Otherwise the first argument names the command and the rest are its options.
/// </summary>
/// <param name="Command">The command to run.</param>
/// <param name="HostArgs">Arguments for the host configuration (only for <see cref="JobCommand.Run"/>).</param>
/// <param name="Seed">Options of <see cref="JobCommand.Seed"/>; <see langword="null"/> for other commands.</param>
public sealed record JobCommandLine(JobCommand Command, IReadOnlyList<string> HostArgs, SeedOptions? Seed)
{
    public const string SeedCommandName = "seed";

    /// <summary>Usage text of the command line, shown when it is invalid.</summary>
    public static readonly string Usage =
        $"""
        Usage: CardPlatform.Settlement.Job [command] [options]
        Commands:
          (none)  run the settlement job
          {SeedCommandName}    populate the ledger with generated purchases, then exit

        {SeedOptions.Usage}
        """;

    /// <summary>Parses <paramref name="args"/>; <paramref name="defaultSeedDate"/> is the default day of a seed.</summary>
    /// <returns>The command line, or <see langword="null"/> with <paramref name="error"/> describing what is wrong.</returns>
    public static JobCommandLine? Parse(IReadOnlyList<string> args, DateOnly defaultSeedDate, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || args[0].StartsWith('-'))
        {
            error = null;
            return new JobCommandLine(JobCommand.Run, args, Seed: null);
        }

        if (args[0] == SeedCommandName)
        {
            SeedOptions? options = SeedOptions.Parse([.. args.Skip(1)], defaultSeedDate, out error);
            return options is null ? null : new JobCommandLine(JobCommand.Seed, [], options);
        }

        error = $"Unknown command '{args[0]}'.";
        return null;
    }
}
