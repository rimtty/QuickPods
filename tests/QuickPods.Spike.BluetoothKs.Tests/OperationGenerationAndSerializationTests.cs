using QuickPods.Spike.BluetoothKs.Observation;
using QuickPods.Spike.BluetoothKs.Operations;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class OperationGenerationAndSerializationTests
{
    private static readonly TimeSpan TestCompletionTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void NewRequestSupersedesOnlyTheSameContainer()
    {
        var generations = new OperationGenerationRegistry();
        long first = generations.Begin("container-a");
        long other = generations.Begin("container-b");
        long second = generations.Begin("container-a");

        Assert.False(generations.IsCurrent("container-a", first));
        Assert.True(generations.IsCurrent("container-a", second));
        Assert.True(generations.IsCurrent("container-b", other));
        Assert.False(generations.TryAccept(Result("container-a", first)));
        Assert.True(generations.TryAccept(Result("container-a", second)));
    }

    [Fact]
    public async Task SameContainerIsSerializedWhileDifferentContainerCanProceed()
    {
        var serializer = new ContainerOperationSerializer();
        ContainerOperationLease first = await serializer.AcquireAsync("container-a", CancellationToken.None);
        Task<ContainerOperationLease> queued = serializer.AcquireAsync(
            "container-a",
            CancellationToken.None);
        ContainerOperationLease other = await serializer.AcquireAsync(
            "container-b",
            CancellationToken.None);

        Assert.False(queued.IsCompleted);

        await first.DisposeAsync();
        await using ContainerOperationLease second = await queued.WaitAsync(TestCompletionTimeout);
        await other.DisposeAsync();
    }

    [Fact]
    public async Task LaneRemainsHeldUntilTimedOutSynchronousCallActuallyCompletes()
    {
        var serializer = new ContainerOperationSerializer();
        var nativeCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ContainerOperationLease first = await serializer.AcquireAsync("container-a", CancellationToken.None);
        first.HoldUntil(nativeCompletion.Task);
        await first.DisposeAsync();

        Task<ContainerOperationLease> queued = serializer.AcquireAsync(
            "container-a",
            CancellationToken.None);
        Assert.False(queued.IsCompleted);

        nativeCompletion.SetResult();
        await using ContainerOperationLease second = await queued.WaitAsync(TestCompletionTimeout);
    }

    private static BluetoothOperationResult Result(string containerKey, long generation)
    {
        var request = new BluetoothOperationRequest(
            containerKey,
            BluetoothOperationKind.Connect,
            generation,
            StartedTimestamp: 0);
        return new BluetoothOperationResult(
            request,
            BluetoothOperationOutcome.Succeeded,
            BluetoothAudioState.Connected,
            KsCallWatchdogStatus.Completed,
            KsHResult: 0,
            KsRequestIssued: true,
            Elapsed: TimeSpan.Zero);
    }
}
