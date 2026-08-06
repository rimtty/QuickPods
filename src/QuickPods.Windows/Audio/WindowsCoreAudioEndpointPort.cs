using System.Runtime.InteropServices;
using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Windows.Audio.Interop;

namespace QuickPods.Windows.Audio;

public sealed class WindowsCoreAudioEndpointPort : IAudioEndpointPort, IDisposable
{
    public static readonly Guid EventContext = new("2E6E886D-8CE5-4A42-9A37-3BF981C7AA15");

    private readonly MtaAudioWorker worker;
    private readonly CoreAudioSession session;
    private int disposed;

    public WindowsCoreAudioEndpointPort()
    {
        worker = new MtaAudioWorker();
        try
        {
            session = worker.Invoke(() => new CoreAudioSession(worker, Publish));
        }
        catch
        {
            worker.Dispose();
            throw;
        }
    }

    public event EventHandler<AudioStateChangedEventArgs>? StateChanged;

    public ValueTask<AudioState> ReadAsync(CancellationToken cancellationToken) =>
        InvokeAsync(session.GetSnapshot, cancellationToken);

    public ValueTask<AudioState> SetVolumeAsync(
        int volumePercent,
        CancellationToken cancellationToken) =>
        InvokeAsync(() => session.SetVolume(VolumeMath.ClampPercent(volumePercent)), cancellationToken);

    public ValueTask<AudioState> SetMuteAsync(bool isMuted, CancellationToken cancellationToken) =>
        InvokeAsync(() => session.SetMute(isMuted), cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            worker.Invoke(session.Dispose);
        }
        finally
        {
            worker.Dispose();
        }
    }

    private ValueTask<AudioState> InvokeAsync(
        Func<AudioState> action,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return worker.InvokeAsync(action, cancellationToken);
    }

    private void Publish(AudioState state, bool isSelfOriginated)
    {
        StateChanged?.Invoke(this, new AudioStateChangedEventArgs(state, isSelfOriginated));
    }

    private sealed class CoreAudioSession : IDisposable
    {
        private const int ElementNotFound = unchecked((int)0x80070490);
        private const ushort VariantTypeWideString = 31;

        private static readonly int[] RebindRetryMilliseconds = [0, 50, 100, 250, 500];
        private static readonly PropertyKey FriendlyNameProperty = new(
            new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
            14);

        private readonly MtaAudioWorker worker;
        private readonly Action<AudioState, bool> stateSink;
        private readonly AudioRole role = AudioRole.Console;
        private readonly IMMDeviceEnumerator enumerator;
        private readonly DefaultDeviceNotificationClient deviceNotificationClient;
        private readonly Timer recoveryTimer;
        private nint deviceNotificationPointer;
        private EndpointBinding? binding;
        private bool deviceNotificationsRegistered;
        private long generation;
        private int disposed;

        public CoreAudioSession(MtaAudioWorker worker, Action<AudioState, bool> stateSink)
        {
            this.worker = worker;
            this.stateSink = stateSink;
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            deviceNotificationClient = new DefaultDeviceNotificationClient(
                role,
                worker.TryPost,
                RebindDefaultEndpointSafely,
                HandleDeviceStateChanged);
            recoveryTimer = new Timer(
                _ => worker.TryPost(RebindDefaultEndpointSafely),
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            deviceNotificationPointer = Marshal.GetComInterfaceForObject(
                deviceNotificationClient,
                typeof(IMMNotificationClient));

            try
            {
                TryRegisterDeviceNotifications();
                RebindDefaultEndpointSafely();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public AudioState GetSnapshot()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (binding is null)
            {
                RebindDefaultEndpointSafely();
            }

            if (binding is null)
            {
                return UnavailableSnapshot();
            }

            try
            {
                return ReadSnapshot(binding);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                RebindDefaultEndpointSafely();
                return binding is null ? UnavailableSnapshot() : TryReadSnapshot(binding);
            }
        }

        public AudioState SetVolume(int volumePercent)
        {
            EndpointBinding? current = GetCurrentBinding();
            if (current is null)
            {
                return UnavailableSnapshot();
            }

            try
            {
                Guid context = EventContext;
                HResult.ThrowIfFailed(
                    current.Volume.SetMasterVolumeLevelScalar(
                        VolumeMath.PercentToScalar(volumePercent),
                        ref context),
                    nameof(IAudioEndpointVolume.SetMasterVolumeLevelScalar));
                return ReadSnapshot(current);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                RebindDefaultEndpointSafely();
                return binding is null ? UnavailableSnapshot() : TryReadSnapshot(binding);
            }
        }

        public AudioState SetMute(bool isMuted)
        {
            EndpointBinding? current = GetCurrentBinding();
            if (current is null)
            {
                return UnavailableSnapshot();
            }

            try
            {
                Guid context = EventContext;
                HResult.ThrowIfFailed(
                    current.Volume.SetMute(isMuted, ref context),
                    nameof(IAudioEndpointVolume.SetMute));
                return ReadSnapshot(current);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                RebindDefaultEndpointSafely();
                return binding is null ? UnavailableSnapshot() : TryReadSnapshot(binding);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            if (deviceNotificationsRegistered)
            {
                _ = enumerator.UnregisterEndpointNotificationCallback(deviceNotificationClient);
                deviceNotificationsRegistered = false;
            }

            recoveryTimer.Dispose();

            if (deviceNotificationPointer != nint.Zero)
            {
                _ = Marshal.Release(deviceNotificationPointer);
                deviceNotificationPointer = nint.Zero;
            }

            RetireCurrentBinding();
            ReleaseComObject(enumerator);
        }

        private EndpointBinding? GetCurrentBinding()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (binding is null)
            {
                RebindDefaultEndpointSafely();
            }

            return binding;
        }

        private void RebindDefaultEndpointSafely()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            RetireCurrentBinding();
            generation = checked(generation + 1);
            TryRegisterDeviceNotifications();

            foreach (int delayMilliseconds in RebindRetryMilliseconds)
            {
                if (delayMilliseconds != 0)
                {
                    Thread.Sleep(delayMilliseconds);
                }

                IMMDevice? device = null;
                try
                {
                    int result = enumerator.GetDefaultAudioEndpoint(AudioDataFlow.Render, role, out device);
                    if (result == ElementNotFound)
                    {
                        continue;
                    }

                    HResult.ThrowIfFailed(result, nameof(IMMDeviceEnumerator.GetDefaultAudioEndpoint));
                    IMMDevice ownedDevice = device;
                    device = null;
                    EndpointBinding candidate = CreateBinding(ownedDevice, generation);
                    binding = candidate;
                    AudioState snapshot = ReadSnapshot(candidate);
                    _ = recoveryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    stateSink(snapshot, false);
                    return;
                }
                catch (Exception exception) when (IsRecoverable(exception))
                {
                    if (device is not null)
                    {
                        ReleaseComObject(device);
                    }

                    RetireCurrentBinding();
                }
            }

            _ = recoveryTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            stateSink(UnavailableSnapshot(), false);
        }

        private void TryRegisterDeviceNotifications()
        {
            if (deviceNotificationsRegistered)
            {
                return;
            }

            try
            {
                HResult.ThrowIfFailed(
                    enumerator.RegisterEndpointNotificationCallback(deviceNotificationClient),
                    nameof(IMMDeviceEnumerator.RegisterEndpointNotificationCallback));
                deviceNotificationsRegistered = true;
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                _ = recoveryTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            }
        }

        private EndpointBinding CreateBinding(IMMDevice device, long bindingGeneration)
        {
            try
            {
                HResult.ThrowIfFailed(device.GetId(out string endpointId), nameof(IMMDevice.GetId));
                string displayName = ReadFriendlyName(device);
                IAudioEndpointVolume volume = ActivateEndpointVolume(device);
                var callback = new AudioEndpointVolumeCallback(
                    bindingGeneration,
                    worker.TryPost,
                    HandleVolumeNotification);

                try
                {
                    HResult.ThrowIfFailed(
                        volume.RegisterControlChangeNotify(callback),
                        nameof(IAudioEndpointVolume.RegisterControlChangeNotify));
                    return new EndpointBinding(
                        bindingGeneration,
                        endpointId,
                        displayName,
                        device,
                        volume,
                        callback);
                }
                catch
                {
                    ReleaseComObject(volume);
                    throw;
                }
            }
            catch
            {
                ReleaseComObject(device);
                throw;
            }
        }

        private void HandleDeviceStateChanged(string deviceId, AudioDeviceState state)
        {
            if (binding is not null &&
                string.Equals(binding.EndpointId, deviceId, StringComparison.Ordinal) &&
                !state.HasFlag(AudioDeviceState.Active))
            {
                RebindDefaultEndpointSafely();
            }
        }

        private void HandleVolumeNotification(NativeVolumeNotification notification)
        {
            if (binding is null || notification.Generation != generation)
            {
                return;
            }

            var snapshot = new AudioState(
                AudioCapability.Available,
                VolumeMath.ScalarToPercent(notification.VolumeScalar),
                notification.IsMuted,
                binding.DisplayName)
            {
                Generation = notification.Generation,
            };
            stateSink(snapshot, notification.EventContext == EventContext);
        }

        private AudioState ReadSnapshot(EndpointBinding current)
        {
            HResult.ThrowIfFailed(
                current.Volume.GetMasterVolumeLevelScalar(out float scalar),
                nameof(IAudioEndpointVolume.GetMasterVolumeLevelScalar));
            HResult.ThrowIfFailed(
                current.Volume.GetMute(out bool isMuted),
                nameof(IAudioEndpointVolume.GetMute));
            HResult.ThrowIfFailed(current.Device.GetState(out AudioDeviceState state), nameof(IMMDevice.GetState));
            if (!state.HasFlag(AudioDeviceState.Active))
            {
                throw new CoreAudioInteropException(nameof(IMMDevice.GetState), unchecked((int)0x88890004));
            }

            return new AudioState(
                AudioCapability.Available,
                VolumeMath.ScalarToPercent(scalar),
                isMuted,
                current.DisplayName)
            {
                Generation = current.Generation,
            };
        }

        private AudioState TryReadSnapshot(EndpointBinding current)
        {
            try
            {
                return ReadSnapshot(current);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                RetireCurrentBinding();
                return UnavailableSnapshot();
            }
        }

        private AudioState UnavailableSnapshot() =>
            AudioState.Unavailable with { Generation = generation };

        private void RetireCurrentBinding()
        {
            EndpointBinding? previous = binding;
            binding = null;
            previous?.Dispose();
        }

        private static IAudioEndpointVolume ActivateEndpointVolume(IMMDevice device)
        {
            Guid interfaceId = typeof(IAudioEndpointVolume).GUID;
            HResult.ThrowIfFailed(
                device.Activate(
                    ref interfaceId,
                    ComClassContext.All,
                    nint.Zero,
                    out object activatedInterface),
                nameof(IMMDevice.Activate));
            return (IAudioEndpointVolume)activatedInterface;
        }

        private static string ReadFriendlyName(IMMDevice device)
        {
            IPropertyStore? properties = null;
            PropVariant value = default;
            try
            {
                HResult.ThrowIfFailed(device.OpenPropertyStore(0, out properties), nameof(IMMDevice.OpenPropertyStore));
                PropertyKey key = FriendlyNameProperty;
                HResult.ThrowIfFailed(properties.GetValue(ref key, out value), nameof(IPropertyStore.GetValue));
                return value.VariantType == VariantTypeWideString && value.PointerValue != nint.Zero
                    ? Marshal.PtrToStringUni(value.PointerValue) ?? "既定の出力デバイス"
                    : "既定の出力デバイス";
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                return "既定の出力デバイス";
            }
            finally
            {
                _ = CoreAudioNativeMethods.PropVariantClear(ref value);
                if (properties is not null)
                {
                    ReleaseComObject(properties);
                }
            }
        }

        private static bool IsRecoverable(Exception exception) =>
            exception is CoreAudioInteropException or COMException;

        private static void ReleaseComObject(object value)
        {
            if (Marshal.IsComObject(value))
            {
                _ = Marshal.ReleaseComObject(value);
            }
        }

        private sealed class EndpointBinding : IDisposable
        {
            private bool callbackRegistered = true;
            private int disposed;

            public EndpointBinding(
                long generation,
                string endpointId,
                string displayName,
                IMMDevice device,
                IAudioEndpointVolume volume,
                AudioEndpointVolumeCallback callback)
            {
                Generation = generation;
                EndpointId = endpointId;
                DisplayName = displayName;
                Device = device;
                Volume = volume;
                Callback = callback;
            }

            public long Generation { get; }

            public string EndpointId { get; }

            public string DisplayName { get; }

            public IMMDevice Device { get; }

            public IAudioEndpointVolume Volume { get; }

            public AudioEndpointVolumeCallback Callback { get; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0)
                {
                    return;
                }

                if (callbackRegistered)
                {
                    _ = Volume.UnregisterControlChangeNotify(Callback);
                    callbackRegistered = false;
                }

                ReleaseComObject(Volume);
                ReleaseComObject(Device);
            }
        }
    }
}
