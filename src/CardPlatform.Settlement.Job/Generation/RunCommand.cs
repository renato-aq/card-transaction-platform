using CardPlatform.Settlement.Job.Measurement;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// The <c>run</c> command, also what the job does without arguments: writes the settlement file with the
/// <see cref="ISettlementFileGenerator"/>, measures what that cost (<see cref="RunMeasurement"/>) and logs a summary
/// (<see cref="RunMetrics"/>), also written as JSON when <see cref="RunOptions.MetricsOutputPath"/> is given. Failures
/// are logged and turned into a non-zero exit code; a failed run leaves no settlement file and no metrics file behind.
/// </summary>
public sealed partial class RunCommand(ISettlementFileGenerator generator, ILogger<RunCommand> logger)
{
    /// <summary>Writes the settlement file and returns the process exit code: 0 on success, 1 on any failure.</summary>
    public async Task<int> RunAsync(RunOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        RunMetrics metrics;
        try
        {
            string outputDirectory = Path.GetFullPath(options.OutputDirectory);
            if (options.MetricsOutputPath is not null)
            {
                RunMetricsFile.DeleteStale(options.MetricsOutputPath);
            }

            LogStarting(options.Date, options.Currency, outputDirectory);

            // The measurement covers the whole generation: reading, formatting and writing.
            SettlementFileResult result;
            RunCost cost;
            using (RunMeasurement measurement = RunMeasurement.Start())
            {
                result = await generator.GenerateAsync(options, cancellationToken).ConfigureAwait(false);
                cost = measurement.Stop();
            }

            LogFinished(result.Path, result.Trailer.DetailCount, result.Trailer.TotalAmount, options.Currency);
            metrics = RunMetrics.Create(
                generator.Name,
                options.Date,
                options.Currency,
                result.Trailer.DetailCount,
                new FileInfo(result.Path).Length,
                cost);
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

        LogSummary(
            metrics.Strategy,
            metrics.Date,
            metrics.Currency,
            metrics.RecordCount,
            metrics.FileSizeBytes,
            metrics.ElapsedMilliseconds,
            metrics.CpuTimeMilliseconds,
            metrics.PeakWorkingSetBytes,
            metrics.AllocatedBytes);

        return options.MetricsOutputPath is null
            ? ExitCodes.Success
            : await WriteMetricsAsync(options.MetricsOutputPath, metrics, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> WriteMetricsAsync(string path, RunMetrics metrics, CancellationToken cancellationToken)
    {
        try
        {
            string fullPath = await RunMetricsFile.WriteAsync(path, metrics, cancellationToken).ConfigureAwait(false);
            LogMetricsWritten(fullPath);
            return ExitCodes.Success;
        }
        catch (Exception exception)
        {
            // The settlement file is written; only its metrics are missing.
            LogMetricsFailed(exception, Path.GetFullPath(path));
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Run summary: strategy {Strategy}, date {Date:yyyy-MM-dd}, currency {Currency}, {RecordCount} records, "
            + "file {FileSizeBytes} bytes, elapsed {ElapsedMilliseconds} ms, CPU {CpuTimeMilliseconds} ms, "
            + "peak working set {PeakWorkingSetBytes} bytes, allocated {AllocatedBytes} bytes")]
    private partial void LogSummary(
        string strategy,
        DateOnly date,
        string currency,
        long recordCount,
        long fileSizeBytes,
        double elapsedMilliseconds,
        double cpuTimeMilliseconds,
        long peakWorkingSetBytes,
        long allocatedBytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run metrics written: {Path}")]
    private partial void LogMetricsWritten(string path);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The settlement file was written, but its metrics could not be written to {Path}")]
    private partial void LogMetricsFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Settlement cancelled; no settlement file was written")]
    private partial void LogCancelled();

    [LoggerMessage(Level = LogLevel.Error, Message = "Settlement failed; no settlement file was written")]
    private partial void LogFailed(Exception exception);
}
