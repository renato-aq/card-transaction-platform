using CardPlatform.Settlement.Job.Seeding;

namespace CardPlatform.Settlement.Job;

/// <summary>
/// Entry point and composition root of the job, shared by <c>Program</c> and the integration tests so both run the same
/// code: parses the command line (<see cref="JobCommandLine"/>), builds the host for the command, runs it and returns
/// the process exit code (<see cref="ExitCodes"/>).
/// </summary>
public static partial class SettlementJobHost
{
    /// <summary>Name of the connection string declared by the AppHost (<c>postgres.AddDatabase("ledgerdb")</c>).</summary>
    public const string LedgerConnectionName = "ledgerdb";

    /// <summary>
    /// Runs the command given by <paramref name="args"/>. <paramref name="configure"/> lets tests adjust the host before
    /// any service is registered, e.g. to set the connection string.
    /// </summary>
    public static async Task<int> RunAsync(string[] args, Action<HostApplicationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        // Purchases are settled the day after; seeding yesterday by default gives the job a day to settle.
        DateOnly yesterday = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime).AddDays(-1);
        JobCommandLine? commandLine = JobCommandLine.Parse(args, yesterday, out string? error);
        if (commandLine is null)
        {
            await Console.Error.WriteLineAsync($"{error}{Environment.NewLine}{Environment.NewLine}{JobCommandLine.Usage}")
                .ConfigureAwait(false);
            return ExitCodes.InvalidArguments;
        }

        return commandLine.Command switch
        {
            JobCommand.Seed => await SeedAsync(commandLine.Seed!, configure).ConfigureAwait(false),
            _ => await RunJobAsync([.. commandLine.HostArgs], configure).ConfigureAwait(false),
        };
    }

    private static async Task<int> RunJobAsync(string[] hostArgs, Action<HostApplicationBuilder>? configure)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(hostArgs);
        configure?.Invoke(builder);

        builder.AddServiceDefaults();
        builder.Services.AddHostedService<SettlementJob>();

        using IHost host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task<int> SeedAsync(SeedOptions options, Action<HostApplicationBuilder>? configure)
    {
        // The command-line arguments are the seed options, not host settings.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder([]);
        configure?.Invoke(builder);

        builder.AddServiceDefaults();
        builder.AddNpgsqlDataSource(LedgerConnectionName);
        builder.Services.AddSingleton<LedgerSeeder>();
        builder.Services.AddSingleton<SeedCommand>();

        using IHost host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);
        try
        {
            // Ctrl+C stops the application, which cancels the seed.
            CancellationToken stopping = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

            // Resolving the data source without a connection string throws; fail with a clear message instead.
            if (!HasLedgerConnectionString(host.Services.GetRequiredService<IConfiguration>()))
            {
                ILogger logger = host.Services.GetRequiredService<ILogger<SeedCommand>>();
                LogConnectionStringMissing(logger, LedgerConnectionName);
                return ExitCodes.Failure;
            }

            return await host.Services.GetRequiredService<SeedCommand>().RunAsync(options, stopping).ConfigureAwait(false);
        }
        finally
        {
            await host.StopAsync().ConfigureAwait(false);
        }
    }

    // Aspire.Npgsql reads ConnectionStrings:{name} first and falls back to Aspire:Npgsql:ConnectionString.
    private static bool HasLedgerConnectionString(IConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration.GetConnectionString(LedgerConnectionName))
            || !string.IsNullOrWhiteSpace(configuration["Aspire:Npgsql:ConnectionString"]);
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The connection string '{ConnectionName}' is missing; nothing was seeded. Start the job from the Aspire "
            + "AppHost, which provides it, or set it in the environment variable ConnectionStrings__ledgerdb")]
    private static partial void LogConnectionStringMissing(ILogger logger, string connectionName);
}
