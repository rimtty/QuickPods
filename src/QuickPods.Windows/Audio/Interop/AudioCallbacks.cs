using System.Runtime.InteropServices;

namespace QuickPods.Windows.Audio.Interop;

internal sealed record NativeVolumeNotification(
    long Generation,
    Guid EventContext,
    bool IsMuted,
    float VolumeScalar);

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class AudioEndpointVolumeCallback : IAudioEndpointVolumeCallback
{
    private const int EFail = unchecked((int)0x80004005);
    private const int EPointer = unchecked((int)0x80004003);

    private readonly long generation;
    private readonly Func<Action, bool> post;
    private readonly Action<NativeVolumeNotification> sink;
    private NativeVolumeNotification? latest;
    private int workQueued;

    public AudioEndpointVolumeCallback(
        long generation,
        Func<Action, bool> post,
        Action<NativeVolumeNotification> sink)
    {
        this.generation = generation;
        this.post = post;
        this.sink = sink;
    }

    public int OnNotify(nint notificationData)
    {
        if (notificationData == nint.Zero)
        {
            return EPointer;
        }

        try
        {
            AudioVolumeNotificationData native =
                Marshal.PtrToStructure<AudioVolumeNotificationData>(notificationData);
            Interlocked.Exchange(
                ref latest,
                new NativeVolumeNotification(
                    generation,
                    native.EventContext,
                    native.IsMuted,
                    native.MasterVolume));
            if (Interlocked.Exchange(ref workQueued, 1) == 0 && !post(DrainLatest))
            {
                Volatile.Write(ref workQueued, 0);
                return EFail;
            }

            return 0;
        }
        catch
        {
            return EFail;
        }
    }

    private void DrainLatest()
    {
        try
        {
            NativeVolumeNotification? notification = Interlocked.Exchange(ref latest, null);
            if (notification is not null)
            {
                sink(notification);
            }
        }
        finally
        {
            Volatile.Write(ref workQueued, 0);
            if (Volatile.Read(ref latest) is not null &&
                Interlocked.Exchange(ref workQueued, 1) == 0 &&
                !post(DrainLatest))
            {
                Volatile.Write(ref workQueued, 0);
            }
        }
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class DefaultDeviceNotificationClient : IMMNotificationClient
{
    private const int EFail = unchecked((int)0x80004005);

    private readonly AudioRole role;
    private readonly Func<Action, bool> post;
    private readonly Action defaultEndpointChanged;
    private readonly Action<string, AudioDeviceState> deviceStateChanged;
    private DeviceStateSignal? latestDeviceState;
    private int defaultChangeQueued;
    private int deviceStateWorkQueued;

    public DefaultDeviceNotificationClient(
        AudioRole role,
        Func<Action, bool> post,
        Action defaultEndpointChanged,
        Action<string, AudioDeviceState> deviceStateChanged)
    {
        this.role = role;
        this.post = post;
        this.defaultEndpointChanged = defaultEndpointChanged;
        this.deviceStateChanged = deviceStateChanged;
    }

    public int OnDeviceStateChanged(string deviceId, AudioDeviceState newState)
    {
        Interlocked.Exchange(ref latestDeviceState, new DeviceStateSignal(deviceId, newState));
        if (Interlocked.Exchange(ref deviceStateWorkQueued, 1) == 0 && !post(DrainLatestDeviceState))
        {
            Volatile.Write(ref deviceStateWorkQueued, 0);
            return EFail;
        }

        return 0;
    }

    public int OnDeviceAdded(string deviceId) => 0;

    public int OnDeviceRemoved(string deviceId) => 0;

    public int OnDefaultDeviceChanged(AudioDataFlow dataFlow, AudioRole eventRole, string? defaultDeviceId)
    {
        if (dataFlow != AudioDataFlow.Render || eventRole != role)
        {
            return 0;
        }

        if (Interlocked.Exchange(ref defaultChangeQueued, 1) != 0)
        {
            return 0;
        }

        if (!post(() =>
        {
            Volatile.Write(ref defaultChangeQueued, 0);
            defaultEndpointChanged();
        }))
        {
            Volatile.Write(ref defaultChangeQueued, 0);
            return EFail;
        }

        return 0;
    }

    public int OnPropertyValueChanged(string deviceId, PropertyKey propertyKey) => 0;

    private void DrainLatestDeviceState()
    {
        try
        {
            DeviceStateSignal? signal = Interlocked.Exchange(ref latestDeviceState, null);
            if (signal is not null)
            {
                deviceStateChanged(signal.DeviceId, signal.State);
            }
        }
        finally
        {
            Volatile.Write(ref deviceStateWorkQueued, 0);
            if (Volatile.Read(ref latestDeviceState) is not null &&
                Interlocked.Exchange(ref deviceStateWorkQueued, 1) == 0 &&
                !post(DrainLatestDeviceState))
            {
                Volatile.Write(ref deviceStateWorkQueued, 0);
            }
        }
    }

    private sealed record DeviceStateSignal(string DeviceId, AudioDeviceState State);
}
