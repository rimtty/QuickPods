using System.Diagnostics;

namespace QuickPods.Spike.CoreAudio;

internal sealed record ResourceSample(
    int Iteration,
    long Timestamp,
    long PrivateBytes,
    long HandleCount,
    long ThreadCount)
{
    public static ResourceSample Capture(int iteration)
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        return new ResourceSample(
            iteration,
            Stopwatch.GetTimestamp(),
            process.PrivateMemorySize64,
            process.HandleCount,
            process.Threads.Count);
    }

    public static ResourceSample CaptureLiveResources(int iteration)
    {
        // The exercise creates short-lived Tasks and cancellation registrations.
        // Collect those before sampling so the trend represents live resources,
        // including any leaked native wrappers, rather than benchmark bookkeeping.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return Capture(iteration);
    }
}
