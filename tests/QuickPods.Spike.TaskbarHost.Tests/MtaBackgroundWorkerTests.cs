using QuickPods.Spike.TaskbarHost.Runtime;

namespace QuickPods.Spike.TaskbarHost.Tests;

[Collection(NativeHostTestGroup.Name)]
public sealed class MtaBackgroundWorkerTests
{
    [Fact]
    public async Task RunAsync_ExecutesOnDedicatedMtaThread()
    {
        int callerThread = Environment.CurrentManagedThreadId;

        (int WorkerThread, ApartmentState Apartment) = await MtaBackgroundWorker.RunAsync(
            () => (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.NotEqual(callerThread, WorkerThread);
        Assert.Equal(ApartmentState.MTA, Apartment);
    }

    [Fact]
    public async Task RunAsync_PreCanceledTokenDoesNotStartWorker()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            MtaBackgroundWorker.RunAsync(
                () => 42,
                TimeSpan.FromSeconds(2),
                cancellation.Token));
    }

    [Fact]
    public async Task TimedOutWorker_KeepsSingleOperationGateClosedUntilCallbackReturns()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> timedOut = MtaBackgroundWorker.RunAsync(
            () =>
            {
                entered.TrySetResult();
                try
                {
                    release.Wait(TimeSpan.FromSeconds(5));
                    return 1;
                }
                finally
                {
                    callbackReturned.TrySetResult();
                }
            },
            TimeSpan.FromMilliseconds(50),
            CancellationToken.None);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            _ = await Assert.ThrowsAsync<TimeoutException>(async () => await timedOut);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                MtaBackgroundWorker.RunAsync(
                    () => 2,
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None));
        }
        finally
        {
            release.Set();
            await callbackReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }

        Task<int>? followUp = null;
        for (int attempt = 0; attempt < 100 && followUp is null; attempt++)
        {
            try
            {
                followUp = MtaBackgroundWorker.RunAsync(
                    () => 3,
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                await Task.Delay(10);
            }
        }

        Assert.NotNull(followUp);
        Assert.Equal(3, await followUp);
    }
}
