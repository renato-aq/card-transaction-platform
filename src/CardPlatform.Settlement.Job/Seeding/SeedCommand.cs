using Npgsql;

namespace CardPlatform.Settlement.Job.Seeding;

/// <summary>
/// The <c>seed</c> command: checks that the ledger schema exists, then runs the <see cref="LedgerSeeder"/>. Failures
/// are logged and turned into a non-zero exit code.
/// </summary>
public sealed partial class SeedCommand(NpgsqlDataSource dataSource, LedgerSeeder seeder, ILogger<SeedCommand> logger)
{
    /// <summary>Runs the seed and returns the process exit code: 0 on success, 1 on any failure.</summary>
    public async Task<int> RunAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            IReadOnlyList<string> missingTables =
                await LedgerSchema.FindMissingTablesAsync(dataSource, cancellationToken).ConfigureAwait(false);
            if (missingTables.Count > 0)
            {
                LogSchemaMissing(string.Join(", ", missingTables), new NpgsqlConnectionStringBuilder(dataSource.ConnectionString).Database);
                return ExitCodes.Failure;
            }

            await seeder.SeedAsync(options, cancellationToken).ConfigureAwait(false);
            return ExitCodes.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCancelled();
            return ExitCodes.Failure;
        }
        catch (Exception exception)
        {
            // The process exits right after: any failure, expected or not, ends as a logged error and an exit code.
            LogFailed(exception);
            return ExitCodes.Failure;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The ledger schema was not found in database '{Database}' (missing tables: {MissingTables}). "
            + "Apply the Ledger migrations first, e.g. by starting the Ledger API once in Development; nothing was seeded")]
    private partial void LogSchemaMissing(string missingTables, string? database);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seed cancelled; nothing was seeded")]
    private partial void LogCancelled();

    [LoggerMessage(Level = LogLevel.Error, Message = "Seed failed; nothing was seeded")]
    private partial void LogFailed(Exception exception);
}
