using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuickPods.Spike.CoreAudio.Interop;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class AudioEndpointVolumeCallback : IAudioEndpointVolumeCallback
{
    private const int EFail = unchecked((int)0x80004005);
    private const int EPointer = unchecked((int)0x80004003);

    private readonly long _generation;
    private readonly Func<Action, bool> _post;
    private readonly Action<VolumeNotification> _sink;
    private VolumeNotification? _latest;
    private int _workQueued;

    public AudioEndpointVolumeCallback(
        long generation,
        Func<Action, bool> post,
        Action<VolumeNotification> sink)
    {
        _generation = generation;
        _post = post;
        _sink = sink;
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
            long callbackTimestamp = Stopwatch.GetTimestamp();
            var notification = new VolumeNotification(
                _generation,
                native.EventContext,
                native.IsMuted,
                native.MasterVolume,
                callbackTimestamp);

            Interlocked.Exchange(ref _latest, notification);
            if (Interlocked.Exchange(ref _workQueued, 1) == 0 && !_post(DrainLatest))
            {
                Volatile.Write(ref _workQueued, 0);
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
            VolumeNotification? latest = Interlocked.Exchange(ref _latest, null);
            if (latest is not null)
            {
                _sink(latest);
            }
        }
        finally
        {
            Volatile.Write(ref _workQueued, 0);
            if (Volatile.Read(ref _latest) is not null &&
                Interlocked.Exchange(ref _workQueued, 1) == 0 &&
                !_post(DrainLatest))
            {
                Volatile.Write(ref _workQueued, 0);
            }
        }
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class DefaultDeviceNotificationClient : IMMNotificationClient
{
    private const int EFail = unchecked((int)0x80004005);

    private readonly AudioRole _role;
    private readonly Func<Action, bool> _post;
    private readonly Action _defaultEndpointChanged;
    private readonly Action<string, AudioDeviceState> _deviceStateChanged;
    private DeviceStateSignal? _latestDeviceState;
    private int _defaultChangeQueued;
    private int _deviceStateWorkQueued;

    public DefaultDeviceNotificationClient(
        AudioRole role,
        Func<Action, bool> post,
        Action defaultEndpointChanged,
        Action<string, AudioDeviceState> deviceStateChanged)
    {
        _role = role;
        _post = post;
        _defaultEndpointChanged = defaultEndpointChanged;
        _deviceStateChanged = deviceStateChanged;
    }

    public int OnDeviceStateChanged(string deviceId, AudioDeviceState newState)
    {
        Interlocked.Exchange(ref _latestDeviceState, new DeviceStateSignal(deviceId, newState));
        if (Interlocked.Exchange(ref _deviceStateWorkQueued, 1) == 0 && !_post(DrainLatestDeviceState))
        {
            Volatile.Write(ref _deviceStateWorkQueued, 0);
            return EFail;
        }

        return 0;
    }

    public int OnDeviceAdded(string deviceId)
    {
        return 0;
    }

    public int OnDeviceRemoved(string deviceId)
    {
        return 0;
    }

    public int OnDefaultDeviceChanged(
        AudioDataFlow dataFlow,
        AudioRole role,
        string? defaultDeviceId)
    {
        if (dataFlow != AudioDataFlow.Render || role != _role)
        {
            return 0;
        }

        if (Interlocked.Exchange(ref _defaultChangeQueued, 1) != 0)
        {
            return 0;
        }

        if (!_post(() =>
            {
                Volatile.Write(ref _defaultChangeQueued, 0);
                _defaultEndpointChanged();
            }))
        {
            Volatile.Write(ref _defaultChangeQueued, 0);
            return EFail;
        }

        return 0;
    }

    public int OnPropertyValueChanged(string deviceId, PropertyKey propertyKey)
    {
        return 0;
    }

    private void DrainLatestDeviceState()
    {
        try
        {
            DeviceStateSignal? latest = Interlocked.Exchange(ref _latestDeviceState, null);
            if (latest is not null)
            {
                _deviceStateChanged(latest.DeviceId, latest.State);
            }
        }
        finally
        {
            Volatile.Write(ref _deviceStateWorkQueued, 0);
            if (Volatile.Read(ref _latestDeviceState) is not null &&
                Interlocked.Exchange(ref _deviceStateWorkQueued, 1) == 0 &&
                !_post(DrainLatestDeviceState))
            {
                Volatile.Write(ref _deviceStateWorkQueued, 0);
            }
        }
    }

    private sealed record DeviceStateSignal(string DeviceId, AudioDeviceState State);
}
