using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using QuickPods.Spike.CoreAudio.Interop;

namespace QuickPods.Spike.CoreAudio;

public sealed class CoreAudioClient : IDisposable
{
    public static readonly Guid EventContext = new("2E6E886D-8CE5-4A42-9A37-3BF981C7AA15");

    private readonly MtaAudioWorker _worker;
    private readonly CoreAudioSession _session;
    private int _disposed;

    public CoreAudioClient(
        Action<VolumeNotification>? volumeNotificationSink = null,
        Action<DefaultEndpointChange>? defaultEndpointChangeSink = null,
        Action<Exception>? backgroundFaultSink = null)
    {
        _worker = new MtaAudioWorker();
        if (backgroundFaultSink is not null)
        {
            _worker.BackgroundFaulted += backgroundFaultSink;
        }

        try
        {
            _session = _worker.Invoke(() => new CoreAudioSession(
                _worker,
                volumeNotificationSink,
                defaultEndpointChangeSink));
        }
        catch
        {
            _worker.Dispose();
            throw;
        }
    }

    public AudioSnapshot GetSnapshot()
    {
        ThrowIfDisposed();
        return _worker.Invoke(_session.GetSnapshot);
    }

    public CoreAudioMutationScope BeginMutationScope()
    {
        ThrowIfDisposed();
        MutationRegistration registration = _worker.Invoke(_session.BeginMutation);
        return new CoreAudioMutationScope(this, registration.LeaseId, registration.OriginalSnapshot);
    }

    internal NativeCallTiming SetLeasedVolumePercent(long leaseId, double percent)
    {
        ThrowIfDisposed();
        float scalar = VolumeMath.PercentToScalar(percent);
        return _worker.Invoke(() => _session.SetLeasedVolumeScalar(leaseId, scalar));
    }

    internal NativeCallTiming MuteLeasedEndpoint(long leaseId)
    {
        ThrowIfDisposed();
        return _worker.Invoke(() => _session.MuteLeasedEndpoint(leaseId));
    }

    internal bool RestoreAndEndMutation(long leaseId)
    {
        ThrowIfDisposed();
        return _worker.Invoke(() => _session.RestoreAndEndMutation(leaseId));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _worker.Invoke(_session.Dispose);
        }
        finally
        {
            _worker.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed class CoreAudioSession : IDisposable
    {
        private const int ElementNotFound = unchecked((int)0x80070490);
        private const int RpcDisconnected = unchecked((int)0x80010108);
        private const int AudioDeviceInvalidated = unchecked((int)0x88890004);
        private const int AudioServiceNotRunning = unchecked((int)0x88890010);
        private const int AudioResourcesInvalidated = unchecked((int)0x88890026);

        private static readonly int[] RebindRetryMilliseconds = [0, 50, 100, 250, 500];

        private readonly MtaAudioWorker _worker;
        private readonly Action<VolumeNotification>? _volumeNotificationSink;
        private readonly Action<DefaultEndpointChange>? _defaultEndpointChangeSink;
        private readonly GenerationGate _generation = new();
        private readonly AudioRole _role = AudioRole.Console;
        private readonly IMMDeviceEnumerator _enumerator;
        private readonly DefaultDeviceNotificationClient _deviceNotificationClient;
        private readonly Dictionary<long, MutationLeaseState> _mutationLeases = [];
        private nint _deviceNotificationPointer;
        private EndpointBinding? _binding;
        private bool _deviceNotificationsRegistered;
        private long _nextLeaseId;
        private int _disposed;

        public CoreAudioSession(
            MtaAudioWorker worker,
            Action<VolumeNotification>? volumeNotificationSink,
            Action<DefaultEndpointChange>? defaultEndpointChangeSink)
        {
            _worker = worker;
            _volumeNotificationSink = volumeNotificationSink;
            _defaultEndpointChangeSink = defaultEndpointChangeSink;
            _enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            _deviceNotificationClient = new DefaultDeviceNotificationClient(
                _role,
                worker.TryPost,
                RebindDefaultEndpoint,
                HandleDeviceStateChanged);
            _deviceNotificationPointer = Marshal.GetComInterfaceForObject(
                _deviceNotificationClient,
                typeof(IMMNotificationClient));

            try
            {
                HResult.ThrowIfFailed(
                    _enumerator.RegisterEndpointNotificationCallback(_deviceNotificationClient),
                    nameof(IMMDeviceEnumerator.RegisterEndpointNotificationCallback));
                _deviceNotificationsRegistered = true;
                RebindDefaultEndpoint();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public AudioSnapshot GetSnapshot()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_binding is null)
            {
                return AudioSnapshot.Unavailable(_generation.Current, _role.ToString());
            }

            try
            {
                return ReadSnapshot(_binding);
            }
            catch (CoreAudioInteropException exception) when (IsRebindFailure(exception.NativeHResult))
            {
                RebindDefaultEndpoint();
                return _binding is null
                    ? AudioSnapshot.Unavailable(_generation.Current, _role.ToString())
                    : ReadSnapshot(_binding);
            }
        }

        public MutationRegistration BeginMutation()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_mutationLeases.Count != 0)
            {
                throw new InvalidOperationException("Only one guarded Core Audio mutation may run at a time.");
            }

            EndpointBinding binding = GetBinding();
            AudioSnapshot original = ReadSnapshot(binding);
            binding.AcquireLease();
            long leaseId = checked(++_nextLeaseId);
            _mutationLeases.Add(leaseId, new MutationLeaseState(binding, original));
            return new MutationRegistration(leaseId, original);
        }

        public NativeCallTiming MuteLeasedEndpoint(long leaseId)
        {
            EndpointBinding binding = GetActiveLease(leaseId).Binding;
            long started = Stopwatch.GetTimestamp();
            SetMute(binding.Volume, isMuted: true);
            return new NativeCallTiming(Stopwatch.GetElapsedTime(started));
        }

        public NativeCallTiming SetLeasedVolumeScalar(long leaseId, float scalar)
        {
            EndpointBinding binding = GetActiveLease(leaseId).Binding;
            long started = Stopwatch.GetTimestamp();
            SetVolume(binding.Volume, scalar);
            return new NativeCallTiming(Stopwatch.GetElapsedTime(started));
        }

        public bool RestoreAndEndMutation(long leaseId)
        {
            if (!_mutationLeases.TryGetValue(leaseId, out MutationLeaseState? lease))
            {
                throw new InvalidOperationException("The Core Audio mutation lease is no longer active.");
            }

            try
            {
                return RestoreOriginalEndpoint(lease.Binding, lease.OriginalSnapshot);
            }
            finally
            {
                _mutationLeases.Remove(leaseId);
                lease.Binding.ReleaseLease();
                if (lease.Binding.IsRetired && lease.Binding.LeaseCount == 0)
                {
                    lease.Binding.Dispose();
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            foreach ((long leaseId, MutationLeaseState lease) in _mutationLeases.ToArray())
            {
                try
                {
                    _ = RestoreOriginalEndpoint(lease.Binding, lease.OriginalSnapshot);
                }
                catch
                {
                    // The caller receives restoration failures from the explicit mutation scope.
                }
                finally
                {
                    _mutationLeases.Remove(leaseId);
                    lease.Binding.ReleaseLease();
                    lease.Binding.Dispose();
                }
            }

            if (_deviceNotificationsRegistered)
            {
                _ = _enumerator.UnregisterEndpointNotificationCallback(_deviceNotificationClient);
                _deviceNotificationsRegistered = false;
            }

            if (_deviceNotificationPointer != nint.Zero)
            {
                _ = Marshal.Release(_deviceNotificationPointer);
                _deviceNotificationPointer = nint.Zero;
            }

            _binding?.Dispose();
            _binding = null;
            ReleaseComObject(_enumerator);
        }

        private MutationLeaseState GetActiveLease(long leaseId)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!_mutationLeases.TryGetValue(leaseId, out MutationLeaseState? lease))
            {
                throw new InvalidOperationException("The Core Audio mutation lease is no longer active.");
            }

            if (lease.Binding.IsRetired)
            {
                throw new DefaultAudioEndpointChangedException();
            }

            return lease;
        }

        private EndpointBinding GetBinding()
        {
            return _binding ?? throw new InvalidOperationException(
                "No default console render endpoint is currently available.");
        }

        private AudioSnapshot ReadSnapshot(EndpointBinding binding)
        {
            HResult.ThrowIfFailed(
                binding.Volume.GetMasterVolumeLevelScalar(out float scalar),
                nameof(IAudioEndpointVolume.GetMasterVolumeLevelScalar));
            HResult.ThrowIfFailed(
                binding.Volume.GetMute(out bool isMuted),
                nameof(IAudioEndpointVolume.GetMute));
            HResult.ThrowIfFailed(
                binding.Device.GetState(out AudioDeviceState state),
                nameof(IMMDevice.GetState));

            return new AudioSnapshot(
                binding.Generation,
                binding.EndpointIdHash,
                _role.ToString(),
                (uint)state,
                scalar,
                isMuted,
                IsAvailable: true);
        }

        private void RebindDefaultEndpoint()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            RetireCurrentBinding();
            long generation = _generation.Advance();
            CoreAudioInteropException? lastTransientFailure = null;

            foreach (int delayMilliseconds in RebindRetryMilliseconds)
            {
                if (delayMilliseconds != 0)
                {
                    Thread.Sleep(delayMilliseconds);
                }

                try
                {
                    int result = _enumerator.GetDefaultAudioEndpoint(
                        AudioDataFlow.Render,
                        _role,
                        out IMMDevice device);
                    if (result == ElementNotFound)
                    {
                        lastTransientFailure = null;
                        continue;
                    }

                    HResult.ThrowIfFailed(result, nameof(IMMDeviceEnumerator.GetDefaultAudioEndpoint));
                    _binding = CreateBinding(device, generation);
                    _defaultEndpointChangeSink?.Invoke(new DefaultEndpointChange(
                        generation,
                        _binding.EndpointIdHash,
                        _role.ToString(),
                        Stopwatch.GetTimestamp()));
                    return;
                }
                catch (CoreAudioInteropException exception) when (IsRebindFailure(exception.NativeHResult))
                {
                    lastTransientFailure = exception;
                }
            }

            _defaultEndpointChangeSink?.Invoke(new DefaultEndpointChange(
                generation,
                "none",
                _role.ToString(),
                Stopwatch.GetTimestamp()));

            if (lastTransientFailure is not null)
            {
                throw new InvalidOperationException(
                    "The default Console render endpoint remained transiently unavailable after bounded retries.",
                    lastTransientFailure);
            }
        }

        private EndpointBinding CreateBinding(IMMDevice device, long generation)
        {
            try
            {
                HResult.ThrowIfFailed(device.GetId(out string endpointId), nameof(IMMDevice.GetId));
                IAudioEndpointVolume volume = ActivateEndpointVolume(device);
                var callback = new AudioEndpointVolumeCallback(
                    generation,
                    _worker.TryPost,
                    HandleVolumeNotification);

                try
                {
                    HResult.ThrowIfFailed(
                        volume.RegisterControlChangeNotify(callback),
                        nameof(IAudioEndpointVolume.RegisterControlChangeNotify));
                    return new EndpointBinding(
                        generation,
                        endpointId,
                        HashEndpointId(endpointId),
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

        private void RetireCurrentBinding()
        {
            if (_binding is null)
            {
                return;
            }

            EndpointBinding previous = _binding;
            _binding = null;
            previous.Retire();
            if (previous.LeaseCount == 0)
            {
                previous.Dispose();
            }
        }

        private void HandleDeviceStateChanged(string deviceId, AudioDeviceState state)
        {
            if (_binding is not null &&
                string.Equals(_binding.EndpointId, deviceId, StringComparison.Ordinal) &&
                !state.HasFlag(AudioDeviceState.Active))
            {
                RebindDefaultEndpoint();
            }
        }

        private void HandleVolumeNotification(VolumeNotification notification)
        {
            if (_generation.IsCurrent(notification.Generation))
            {
                _volumeNotificationSink?.Invoke(notification);
            }
        }

        private bool RestoreOriginalEndpoint(EndpointBinding binding, AudioSnapshot original)
        {
            Exception? originalFailure = null;
            try
            {
                if (RestoreAndVerify(binding.Volume, original))
                {
                    return true;
                }

                originalFailure = new InvalidOperationException(
                    "Restoration through the held endpoint interface could not be verified.");
            }
            catch (Exception exception) when (exception is CoreAudioInteropException or COMException)
            {
                originalFailure = exception;
            }

            IMMDevice? reacquiredDevice = null;
            IAudioEndpointVolume? reacquiredVolume = null;
            try
            {
                HResult.ThrowIfFailed(
                    _enumerator.GetDevice(binding.EndpointId, out reacquiredDevice),
                    nameof(IMMDeviceEnumerator.GetDevice));
                reacquiredVolume = ActivateEndpointVolume(reacquiredDevice);
                return RestoreAndVerify(reacquiredVolume, original);
            }
            catch (Exception reacquireFailure)
            {
                throw new AggregateException(
                    "The original audio endpoint could not be restored or verified.",
                    originalFailure ?? new InvalidOperationException(
                        "Restoration through the held endpoint interface failed without an exception."),
                    reacquireFailure);
            }
            finally
            {
                if (reacquiredVolume is not null)
                {
                    ReleaseComObject(reacquiredVolume);
                }

                if (reacquiredDevice is not null)
                {
                    ReleaseComObject(reacquiredDevice);
                }
            }
        }

        private static bool RestoreAndVerify(IAudioEndpointVolume volume, AudioSnapshot original)
        {
            return AudioStateRestorer.RestoreAndVerify(
                new ComAudioStateAccess(volume),
                original.VolumeScalar,
                original.IsMuted);
        }

        private static void SetVolume(IAudioEndpointVolume volume, float scalar)
        {
            Guid context = EventContext;
            HResult.ThrowIfFailed(
                volume.SetMasterVolumeLevelScalar(scalar, ref context),
                nameof(IAudioEndpointVolume.SetMasterVolumeLevelScalar));
        }

        private static void SetMute(IAudioEndpointVolume volume, bool isMuted)
        {
            Guid context = EventContext;
            HResult.ThrowIfFailed(
                volume.SetMute(isMuted, ref context),
                nameof(IAudioEndpointVolume.SetMute));
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

        private static bool IsRebindFailure(int result)
        {
            return result is RpcDisconnected or
                AudioDeviceInvalidated or
                AudioServiceNotRunning or
                AudioResourcesInvalidated;
        }

        private static string HashEndpointId(string endpointId)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(endpointId));
            return Convert.ToHexString(hash.AsSpan(0, 6));
        }

        private static void ReleaseComObject(object value)
        {
            if (Marshal.IsComObject(value))
            {
                _ = Marshal.ReleaseComObject(value);
            }
        }

        private sealed record MutationLeaseState(
            EndpointBinding Binding,
            AudioSnapshot OriginalSnapshot);

        private sealed class ComAudioStateAccess(IAudioEndpointVolume volume) : IAudioStateAccess
        {
            public void SetMuted(bool isMuted)
            {
                SetMute(volume, isMuted);
            }

            public void SetVolumeScalar(float scalar)
            {
                SetVolume(volume, scalar);
            }

            public bool GetMuted()
            {
                HResult.ThrowIfFailed(
                    volume.GetMute(out bool isMuted),
                    nameof(IAudioEndpointVolume.GetMute));
                return isMuted;
            }

            public float GetVolumeScalar()
            {
                HResult.ThrowIfFailed(
                    volume.GetMasterVolumeLevelScalar(out float scalar),
                    nameof(IAudioEndpointVolume.GetMasterVolumeLevelScalar));
                return scalar;
            }
        }

        private sealed class EndpointBinding : IDisposable
        {
            private bool _callbackRegistered = true;
            private int _disposed;

            public EndpointBinding(
                long generation,
                string endpointId,
                string endpointIdHash,
                IMMDevice device,
                IAudioEndpointVolume volume,
                AudioEndpointVolumeCallback callback)
            {
                Generation = generation;
                EndpointId = endpointId;
                EndpointIdHash = endpointIdHash;
                Device = device;
                Volume = volume;
                Callback = callback;
            }

            public long Generation { get; }

            public string EndpointId { get; }

            public string EndpointIdHash { get; }

            public IMMDevice Device { get; }

            public IAudioEndpointVolume Volume { get; }

            public AudioEndpointVolumeCallback Callback { get; }

            public int LeaseCount { get; private set; }

            public bool IsRetired { get; private set; }

            public void AcquireLease()
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                LeaseCount = checked(LeaseCount + 1);
            }

            public void ReleaseLease()
            {
                if (LeaseCount <= 0)
                {
                    throw new InvalidOperationException("The endpoint binding has no active lease.");
                }

                LeaseCount--;
            }

            public void Retire()
            {
                IsRetired = true;
                UnregisterCallback();
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                UnregisterCallback();
                ReleaseComObject(Volume);
                ReleaseComObject(Device);
            }

            private void UnregisterCallback()
            {
                if (_callbackRegistered)
                {
                    _ = Volume.UnregisterControlChangeNotify(Callback);
                    _callbackRegistered = false;
                }
            }
        }
    }
}

internal sealed record MutationRegistration(long LeaseId, AudioSnapshot OriginalSnapshot);

internal sealed class DefaultAudioEndpointChangedException : InvalidOperationException
{
    public DefaultAudioEndpointChangedException()
        : base("The default render endpoint changed during the guarded mutation. The original endpoint will be restored.")
    {
    }
}
