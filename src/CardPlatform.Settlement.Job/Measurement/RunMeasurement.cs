using System.Diagnostics;

namespace CardPlatform.Settlement.Job.Measurement;

/// <summary>
/// Measures what a stretch of the process costs, from <see cref="Start"/> to <see cref="Stop"/>: wall-clock time, CPU
/// time of the whole process (all threads, so it exceeds the wall-clock time when work runs in parallel), bytes
/// allocated by the GC and the peak working set (<see cref="PeakWorkingSetSampler"/>). It only reads counters of the
/// process; it never touches what is being measured.
/// </summary>
public sealed class RunMeasurement : IDisposable
{
    private readonly Process _process;

    private readonly PeakWorkingSetSampler _sampler;

    private readonly long _startTimestamp;

    private readonly TimeSpan _startCpuTime;

    private readonly long _startAllocatedBytes;

    private RunMeasurement(Process process, PeakWorkingSetSampler sampler)
    {
        _process = process;
        _sampler = sampler;
        _startAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true);
        _startCpuTime = CpuTime();
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>Starts measuring now.</summary>
    public static RunMeasurement Start()
    {
        Process process = Process.GetCurrentProcess();
        PeakWorkingSetSampler? sampler = null;
        try
        {
            sampler = PeakWorkingSetSampler.StartForCurrentProcess();
            return new RunMeasurement(process, sampler);
        }
        catch
        {
            sampler?.Dispose();
            process.Dispose();
            throw;
        }
    }

    /// <summary>Stops measuring and returns the cost since <see cref="Start"/>.</summary>
    public RunCost Stop()
    {
        TimeSpan elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        TimeSpan cpuTime = CpuTime() - _startCpuTime;
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - _startAllocatedBytes;
        long peakWorkingSetBytes = _sampler.Stop();
        return new RunCost(elapsed, cpuTime, peakWorkingSetBytes, allocatedBytes);
    }

    /// <summary>Stops the sampling, if still running, and releases the resources.</summary>
    public void Dispose()
    {
        _sampler.Dispose();
        _process.Dispose();
    }

    private TimeSpan CpuTime()
    {
        _process.Refresh();
        return _process.TotalProcessorTime;
    }
}

/// <summary>What a measured stretch of the process cost (<see cref="RunMeasurement"/>).</summary>
/// <param name="Elapsed">Wall-clock time.</param>
/// <param name="CpuTime">CPU time consumed by the process, user and kernel, all threads.</param>
/// <param name="PeakWorkingSetBytes">Highest working set of the process reached, in bytes.</param>
/// <param name="AllocatedBytes">Bytes allocated by the GC, all threads.</param>
public sealed record RunCost(TimeSpan Elapsed, TimeSpan CpuTime, long PeakWorkingSetBytes, long AllocatedBytes);
