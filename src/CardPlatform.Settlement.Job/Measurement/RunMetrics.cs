using System.Text.Json.Serialization;

namespace CardPlatform.Settlement.Job.Measurement;

/// <summary>
/// The summary of one successful <c>run</c>: what was settled and what it cost. Logged at the end of every run and, with
/// <c>--metrics-output</c>, written as JSON (<see cref="RunMetricsFile"/>). The JSON names are a contract read by the
/// benchmark: each name ends with its unit where it has one, and numbers are written in invariant format.
/// </summary>
/// <param name="Strategy">The generation strategy (<c>strategy</c>), e.g. <c>naive</c>.</param>
/// <param name="Date">The settlement date (<c>date</c>), ISO 8601 <c>yyyy-MM-dd</c>.</param>
/// <param name="Currency">ISO 4217 code of the file (<c>currency</c>).</param>
/// <param name="RecordCount">Number of detail records, the count of the trailer (<c>recordCount</c>).</param>
/// <param name="FileSizeBytes">Size of the settlement file, in bytes (<c>fileSizeBytes</c>).</param>
/// <param name="ElapsedMilliseconds">Wall-clock time of the run, in milliseconds (<c>elapsedMilliseconds</c>).</param>
/// <param name="CpuTimeMilliseconds">
/// CPU time consumed by the process during the run, all threads, in milliseconds (<c>cpuTimeMilliseconds</c>).
/// </param>
/// <param name="PeakWorkingSetBytes">
/// Highest working set of the process during the run, in bytes (<c>peakWorkingSetBytes</c>).
/// </param>
/// <param name="AllocatedBytes">Bytes allocated by the GC during the run, all threads (<c>allocatedBytes</c>).</param>
public sealed record RunMetrics(
    [property: JsonPropertyName("strategy")] string Strategy,
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("recordCount")] long RecordCount,
    [property: JsonPropertyName("fileSizeBytes")] long FileSizeBytes,
    [property: JsonPropertyName("elapsedMilliseconds")] double ElapsedMilliseconds,
    [property: JsonPropertyName("cpuTimeMilliseconds")] double CpuTimeMilliseconds,
    [property: JsonPropertyName("peakWorkingSetBytes")] long PeakWorkingSetBytes,
    [property: JsonPropertyName("allocatedBytes")] long AllocatedBytes)
{
    /// <summary>The metrics of a run of <paramref name="strategy"/> that settled <paramref name="recordCount"/> records.</summary>
    public static RunMetrics Create(
        string strategy,
        DateOnly date,
        string currency,
        long recordCount,
        long fileSizeBytes,
        RunCost cost)
    {
        ArgumentNullException.ThrowIfNull(cost);

        return new RunMetrics(
            strategy,
            date,
            currency,
            recordCount,
            fileSizeBytes,
            Milliseconds(cost.Elapsed),
            Milliseconds(cost.CpuTime),
            cost.PeakWorkingSetBytes,
            cost.AllocatedBytes);
    }

    // Microsecond precision is more than enough for a run.
    private static double Milliseconds(TimeSpan duration)
    {
        return Math.Round(duration.TotalMilliseconds, 3);
    }
}
