using System.Runtime.InteropServices;
using QuickPods.Spike.CoreAudio.Interop;

namespace QuickPods.Spike.CoreAudio.Tests;

public sealed class AudioCallbackTests
{
    [Fact]
    public void EndpointCallbackCopiesDataAndQueuesWorkWithoutInvokingSinkInline()
    {
        Guid context = Guid.NewGuid();
        var native = new AudioVolumeNotificationData
        {
            EventContext = context,
            IsMuted = true,
            MasterVolume = 0.42f,
            ChannelCount = 2,
        };
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<AudioVolumeNotificationData>());

        try
        {
            Marshal.StructureToPtr(native, pointer, fDeleteOld: false);
            Action? queuedWork = null;
            VolumeNotification? observed = null;
            var callback = new AudioEndpointVolumeCallback(
                generation: 7,
                work =>
                {
                    queuedWork = work;
                    return true;
                },
                notification => observed = notification);

            int result = callback.OnNotify(pointer);

            Assert.Equal(0, result);
            Assert.Null(observed);
            Assert.NotNull(queuedWork);

            queuedWork();

            Assert.NotNull(observed);
            Assert.Equal(7, observed.Generation);
            Assert.Equal(context, observed.EventContext);
            Assert.True(observed.IsMuted);
            Assert.Equal(0.42f, observed.VolumeScalar, precision: 3);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [Fact]
    public void EndpointCallbackRejectsNullNativeData()
    {
        var callback = new AudioEndpointVolumeCallback(1, _ => true, _ => { });

        Assert.True(callback.OnNotify(nint.Zero) < 0);
    }

    [Fact]
    public void DefaultDeviceCallbackOnlyQueuesSelectedRenderRole()
    {
        int queued = 0;
        var callback = new DefaultDeviceNotificationClient(
            AudioRole.Console,
            work =>
            {
                queued++;
                work();
                return true;
            },
            () => { },
            (_, _) => { });

        Assert.Equal(0, callback.OnDefaultDeviceChanged(AudioDataFlow.Capture, AudioRole.Console, null));
        Assert.Equal(0, callback.OnDefaultDeviceChanged(AudioDataFlow.Render, AudioRole.Multimedia, null));
        Assert.Equal(0, callback.OnDefaultDeviceChanged(AudioDataFlow.Render, AudioRole.Console, null));
        Assert.Equal(1, queued);
    }

    [Fact]
    public void EndpointCallbackCoalescesBurstAndRetainsLatestValue()
    {
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<AudioVolumeNotificationData>());
        try
        {
            Action? queuedWork = null;
            VolumeNotification? observed = null;
            var callback = new AudioEndpointVolumeCallback(
                generation: 3,
                work =>
                {
                    queuedWork ??= work;
                    return true;
                },
                notification => observed = notification);

            for (int index = 0; index < 10_000; index++)
            {
                var native = new AudioVolumeNotificationData
                {
                    EventContext = Guid.Empty,
                    IsMuted = false,
                    MasterVolume = index / 10_000f,
                    ChannelCount = 2,
                };
                Marshal.StructureToPtr(native, pointer, fDeleteOld: false);
                Assert.Equal(0, callback.OnNotify(pointer));
            }

            Assert.NotNull(queuedWork);
            queuedWork();

            Assert.NotNull(observed);
            Assert.Equal(0.9999f, observed.VolumeScalar, precision: 4);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [Fact]
    public void EndpointCallbackCanQueueAgainAfterSinkThrows()
    {
        var queuedWork = new Queue<Action>();
        int sinkCalls = 0;
        var callback = new AudioEndpointVolumeCallback(
            generation: 5,
            work =>
            {
                queuedWork.Enqueue(work);
                return true;
            },
            _ =>
            {
                sinkCalls++;
                if (sinkCalls == 1)
                {
                    throw new InvalidOperationException("Synthetic sink failure.");
                }
            });
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<AudioVolumeNotificationData>());

        try
        {
            Marshal.StructureToPtr(new AudioVolumeNotificationData(), pointer, fDeleteOld: false);
            Assert.Equal(0, callback.OnNotify(pointer));
            Assert.Single(queuedWork);
            Assert.Throws<InvalidOperationException>(() => queuedWork.Dequeue()());

            Assert.Equal(0, callback.OnNotify(pointer));
            Assert.Single(queuedWork);
            queuedWork.Dequeue()();

            Assert.Equal(2, sinkCalls);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [Fact]
    public void DeviceStateCallbackCanQueueAgainAfterSinkThrows()
    {
        var queuedWork = new Queue<Action>();
        int sinkCalls = 0;
        var callback = new DefaultDeviceNotificationClient(
            AudioRole.Console,
            work =>
            {
                queuedWork.Enqueue(work);
                return true;
            },
            () => { },
            (_, _) =>
            {
                sinkCalls++;
                if (sinkCalls == 1)
                {
                    throw new InvalidOperationException("Synthetic sink failure.");
                }
            });

        Assert.Equal(0, callback.OnDeviceStateChanged("endpoint-a", AudioDeviceState.Active));
        Assert.Single(queuedWork);
        Assert.Throws<InvalidOperationException>(() => queuedWork.Dequeue()());

        Assert.Equal(0, callback.OnDeviceStateChanged("endpoint-b", AudioDeviceState.Disabled));
        Assert.Single(queuedWork);
        queuedWork.Dequeue()();

        Assert.Equal(2, sinkCalls);
    }

    [Fact]
    public void InteropHeadersMatchWindowsX64Abi()
    {
        Assert.Equal(28, Marshal.SizeOf<AudioVolumeNotificationData>());
        Assert.Equal(20, Marshal.SizeOf<PropertyKey>());
        Assert.Equal(24, Marshal.SizeOf<PropVariant>());
    }
}
