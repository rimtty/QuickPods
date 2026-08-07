using QuickPods.Windows.Audio.Interop;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class CoreAudioTopologyNotificationTests
{
    [Fact]
    public void DeviceTopologyBurstQueuesOneRefreshAndCanQueueAgainAfterDrain()
    {
        var queuedWork = new Queue<Action>();
        int refreshCount = 0;
        var callback = new DefaultDeviceNotificationClient(
            AudioRole.Console,
            work =>
            {
                queuedWork.Enqueue(work);
                return true;
            },
            () => { },
            (_, _) => { },
            () => refreshCount++);

        Assert.Equal(0, callback.OnDeviceAdded("endpoint-a"));
        Assert.Equal(0, callback.OnDeviceRemoved("endpoint-b"));
        Assert.Single(queuedWork);
        Assert.Equal(0, refreshCount);

        queuedWork.Dequeue()();

        Assert.Equal(1, refreshCount);
        Assert.Equal(0, callback.OnDeviceAdded("endpoint-c"));
        Assert.Single(queuedWork);
    }
}
