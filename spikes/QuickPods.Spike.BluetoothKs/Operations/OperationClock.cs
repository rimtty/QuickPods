using System.Diagnostics;

namespace QuickPods.Spike.BluetoothKs.Operations;

public interface IOperationClock
{
    long GetTimestamp();

    TimeSpan GetElapsedTime(long startingTimestamp);

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemOperationClock : IOperationClock
{
    public static SystemOperationClock Instance { get; } = new();

    private SystemOperationClock()
    {
    }

    public long GetTimestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    public TimeSpan GetElapsedTime(long startingTimestamp)
    {
        return Stopwatch.GetElapsedTime(startingTimestamp);
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}
