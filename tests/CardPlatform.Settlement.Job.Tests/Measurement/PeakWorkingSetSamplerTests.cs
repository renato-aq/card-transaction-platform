using System.Diagnostics;
using System.Runtime.InteropServices;

using CardPlatform.Settlement.Job.Measurement;

namespace CardPlatform.Settlement.Job.Tests.Measurement;

public sealed class PeakWorkingSetSamplerTests
{
    // Long enough that the background thread never samples during a test that drives the samples itself.
    private static readonly TimeSpan NoBackgroundSampling = TimeSpan.FromHours(1);

    [Fact]
    public void The_peak_is_the_highest_sample_not_the_last_one()
    {
        Queue<long> workingSets = new([100, 500, 300, 200]);
        using PeakWorkingSetSampler sampler = new(workingSets.Dequeue, NoBackgroundSampling);

        sampler.Sample();
        sampler.Sample();
        long peak = sampler.Stop();

        Assert.Equal(500, peak);
        Assert.Empty(workingSets); // first sample on start, two in between, last one on stop
    }

    [Fact]
    public void Stopping_twice_takes_no_more_samples_and_keeps_the_peak()
    {
        Queue<long> workingSets = new([700, 400]);
        using PeakWorkingSetSampler sampler = new(workingSets.Dequeue, NoBackgroundSampling);

        Assert.Equal(700, sampler.Stop());
        Assert.Equal(700, sampler.Stop());
    }

    [Fact]
    public void The_background_thread_catches_a_peak_reached_and_left_between_start_and_stop()
    {
        long current = 10;
        using ManualResetEventSlim sampledHigh = new();
        using PeakWorkingSetSampler sampler = new(
            () =>
            {
                long value = Interlocked.Read(ref current);
                if (value == 1_000)
                {
                    sampledHigh.Set();
                }

                return value;
            },
            TimeSpan.FromMilliseconds(5));

        Interlocked.Exchange(ref current, 1_000);
        Assert.True(sampledHigh.Wait(TimeSpan.FromSeconds(10)));
        Interlocked.Exchange(ref current, 20);

        Assert.Equal(1_000, sampler.Stop());
    }

    [Fact]
    public void Sampling_the_current_process_reports_at_least_the_memory_known_to_have_been_in_use()
    {
        const int Size = 256 * 1024 * 1024;
        using PeakWorkingSetSampler sampler = PeakWorkingSetSampler.StartForCurrentProcess(TimeSpan.FromMilliseconds(10));

        // Unmanaged memory, freed before the sampler stops: its last sample need not see it, the background ones do.
        long reached;
        IntPtr memory = Marshal.AllocHGlobal(Size);
        try
        {
            // Touching every page puts it in the working set.
            byte[] chunk = new byte[1024 * 1024];
            Array.Fill(chunk, (byte)1);
            for (int offset = 0; offset < Size; offset += chunk.Length)
            {
                Marshal.Copy(chunk, 0, memory + offset, chunk.Length);
            }

            using Process process = Process.GetCurrentProcess();
            reached = process.WorkingSet64;

            // Several sampling intervals while the memory is in use.
            Thread.Sleep(TimeSpan.FromMilliseconds(200));
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }

        long peak = sampler.Stop();

        Assert.True(reached > Size, $"working set {reached} after touching {Size} bytes");
        // The working set can shrink slightly between two reads as other pages are trimmed.
        Assert.True(peak >= reached - (16 * 1024 * 1024), $"peak {peak}, reached {reached}");
    }
}
