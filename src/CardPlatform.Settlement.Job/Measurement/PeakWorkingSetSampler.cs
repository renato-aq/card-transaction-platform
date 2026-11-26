using System.Diagnostics;

namespace CardPlatform.Settlement.Job.Measurement;

/// <summary>
/// Tracks the highest working set of the process between its creation and <see cref="Stop"/>, by sampling it on a
/// background thread every <see cref="DefaultInterval"/> (or the interval given), plus one sample at the start and one
/// at the end.
/// </summary>
/// <remarks>
/// The operating system's own peak (<see cref="Process.PeakWorkingSet64"/>) is not used: on macOS it is always 0, and on
/// Linux it covers the whole life of the process rather than one run. Sampling gives the same measure on both. A spike
/// shorter than the interval between two samples can be missed; the memory of a settlement run grows and shrinks with
/// the data over seconds, far slower than that. Each sample is one system call, so the overhead is negligible.
/// </remarks>
public sealed class PeakWorkingSetSampler : IDisposable
{
    /// <summary>The interval between two samples of <see cref="StartForCurrentProcess()"/>: 50 ms.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(50);

    private readonly Func<long> _readWorkingSet;

    private readonly TimeSpan _interval;

    private readonly IDisposable? _ownedResource;

    private readonly ManualResetEventSlim _stopping = new();

    private readonly Lock _gate = new();

    private readonly Thread _thread;

    private long _peak;

    private bool _stopped;

    /// <summary>
    /// Takes a first sample with <paramref name="readWorkingSet"/>, which returns a working set in bytes, then starts
    /// sampling every <paramref name="interval"/>.
    /// </summary>
    public PeakWorkingSetSampler(Func<long> readWorkingSet, TimeSpan interval)
        : this(readWorkingSet, interval, ownedResource: null)
    {
    }

    private PeakWorkingSetSampler(Func<long> readWorkingSet, TimeSpan interval, IDisposable? ownedResource)
    {
        ArgumentNullException.ThrowIfNull(readWorkingSet);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        _readWorkingSet = readWorkingSet;
        _interval = interval;
        _ownedResource = ownedResource;

        Sample();
        _thread = new Thread(SampleUntilStopped) { IsBackground = true, Name = "Peak working set sampler" };
        _thread.Start();
    }

    /// <summary>The highest working set sampled so far, in bytes.</summary>
    public long PeakBytes
    {
        get
        {
            lock (_gate)
            {
                return _peak;
            }
        }
    }

    /// <summary>Starts sampling the working set of the current process every <see cref="DefaultInterval"/>.</summary>
    public static PeakWorkingSetSampler StartForCurrentProcess()
    {
        return StartForCurrentProcess(DefaultInterval);
    }

    /// <summary>Starts sampling the working set of the current process every <paramref name="interval"/>.</summary>
    public static PeakWorkingSetSampler StartForCurrentProcess(TimeSpan interval)
    {
        Process process = Process.GetCurrentProcess();
        try
        {
            return new PeakWorkingSetSampler(
                () =>
                {
                    // The values of a Process are cached until refreshed.
                    process.Refresh();
                    return process.WorkingSet64;
                },
                interval,
                process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    /// <summary>Takes one sample now and keeps it if it is the highest so far.</summary>
    public void Sample()
    {
        lock (_gate)
        {
            _peak = Math.Max(_peak, _readWorkingSet());
        }
    }

    /// <summary>Stops the background sampling, takes a last sample and returns the highest one, in bytes.</summary>
    public long Stop()
    {
        if (!_stopped)
        {
            _stopped = true;
            _stopping.Set();
            _thread.Join();
            Sample();
        }

        return PeakBytes;
    }

    /// <summary>Stops the sampling, if still running, and releases the resources.</summary>
    public void Dispose()
    {
        Stop();
        _stopping.Dispose();
        _ownedResource?.Dispose();
    }

    private void SampleUntilStopped()
    {
        while (!_stopping.Wait(_interval))
        {
            Sample();
        }
    }
}
