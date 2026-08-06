using QuickPods.Core.Models;
using QuickPods.Windows.Bluetooth;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class BluetoothOperationGateTests
{
    [Fact]
    public async Task GateRetainsOwnershipUntilAnAsynchronousCompositeOperationSettles()
    {
        string mutexName = $@"Global\QuickPods.Foundation.Tests.{Guid.NewGuid():N}";
        var firstGate = new WindowsBluetoothOperationGate(
            mutexName,
            TimeSpan.FromSeconds(2));
        var secondGate = new WindowsBluetoothOperationGate(
            mutexName,
            TimeSpan.FromMilliseconds(50));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool secondOperationRan = false;

        ValueTask<BluetoothOperationAdmissionResult<int>> first = firstGate.RunAsync(
            async () =>
            {
                entered.SetResult();
                await release.Task;
                return 1;
            },
            CancellationToken.None);
        await entered.Task;

        BluetoothOperationAdmissionResult<int> second = await secondGate.RunAsync(
            () =>
            {
                secondOperationRan = true;
                return ValueTask.FromResult(2);
            },
            CancellationToken.None);

        Assert.Equal(BluetoothOperationAdmissionStatus.Busy, second.Status);
        Assert.False(secondOperationRan);
        release.SetResult();
        BluetoothOperationAdmissionResult<int> completed = await first;
        Assert.Equal(BluetoothOperationAdmissionStatus.Executed, completed.Status);
        Assert.Equal(1, completed.Value);
    }
}
