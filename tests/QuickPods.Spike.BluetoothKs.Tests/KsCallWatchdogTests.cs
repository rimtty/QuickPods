using QuickPods.Spike.BluetoothKs.Operations;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class KsCallWatchdogTests
{
    [Fact]
    public async Task CompletedCallPreservesHResult()
    {
        var clock = new ManualOperationClock();
        var watchdog = new KsCallWatchdog(clock);

        KsCallWatchdogResult result = await watchdog.WaitAsync(
            Task.FromResult(unchecked((int)0x80004005)),
            CancellationToken.None);

        Assert.Equal(KsCallWatchdogStatus.Completed, result.Status);
        Assert.Equal(unchecked((int)0x80004005), result.HResult);
    }

    [Fact]
    public async Task IncompleteSynchronousCallTimesOutAtTheLogicalDeadline()
    {
        var clock = new ManualOperationClock();
        var watchdog = new KsCallWatchdog(clock);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<KsCallWatchdogResult> pending = watchdog.WaitAsync(
            completion.Task,
            CancellationToken.None);
        await WaitUntilAsync(() => clock.PendingDelayCount > 0);
        clock.Advance(KsCallWatchdog.Timeout);
        KsCallWatchdogResult result = await pending.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(KsCallWatchdogStatus.TimedOut, result.Status);
        Assert.Null(result.HResult);
        Assert.Equal(KsCallWatchdog.Timeout, clock.Elapsed);
    }

    [Fact]
    public async Task ContainedProcessTimeoutUsesTheSameTimeoutClassification()
    {
        var watchdog = new KsCallWatchdog(new ManualOperationClock());

        KsCallWatchdogResult result = await watchdog.WaitAsync(
            Task.FromException<int>(new BluetoothKsInvocationTimedOutException()),
            CancellationToken.None);

        Assert.Equal(KsCallWatchdogStatus.TimedOut, result.Status);
        Assert.Null(result.HResult);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (int attempt = 0; attempt < 100 && !predicate(); attempt++)
        {
            await Task.Yield();
        }

        Assert.True(predicate());
    }
}
