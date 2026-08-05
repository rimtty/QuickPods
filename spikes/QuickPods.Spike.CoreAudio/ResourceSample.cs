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
}
