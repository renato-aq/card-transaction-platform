namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// The <c>run</c> command, also what the job does without arguments: writes the settlement file with the
/// <see cref="ISettlementFileGenerator"/>. Failures are logged and turned into a non-zero exit code; a failed run leaves
/// no settlement file behind.
/// </summary>
public sealed partial class RunCommand(ISettlementFileGenerator generator, ILogger<RunCommand> logger)
{
    /// <summary>Writes the settlement file and returns the process exit code: 0 on success, 1 on any failure.</summary>
    public async Task<int> RunAsync(RunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            string outputDirectory = Path.GetFullPath(options.OutputDirectory);
            LogStarting(options.Date, options.Currency, outputDirectory);

            SettlementFileResult result = await generator.GenerateAsync(options, cancellationToken).ConfigureAwait(false);
            LogFinished(result.Path, result.Trailer.DetailCount, result.Trailer.TotalAmount, options.Currency);
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
        Level = LogLevel.Information,
        Message = "Generating the settlement file of {Date} in {Currency} into {OutputDirectory}")]
    private partial void LogStarting(DateOnly date, string currency, string outputDirectory);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Settlement file written: {Path} with {DetailCount} details totalling {TotalAmount} {Currency}")]
    private partial void LogFinished(string path, long detailCount, decimal totalAmount, string currency);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settlement cancelled; no settlement file was written")]
    private partial void LogCancelled();

    [LoggerMessage(Level = LogLevel.Error, Message = "Settlement failed; no settlement file was written")]
    private partial void LogFailed(Exception exception);
}
