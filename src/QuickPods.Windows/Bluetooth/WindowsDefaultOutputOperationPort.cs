using System.Runtime.InteropServices;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Windows.Audio.Interop;

namespace QuickPods.Windows.Bluetooth;

public sealed class WindowsDefaultOutputOperationPort : IDefaultOutputOperationPort, IDisposable
{
    private static readonly AudioRole[] MutableRoles = [AudioRole.Console, AudioRole.Multimedia];
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(5);

    private readonly WindowsBluetoothAudioCatalogPort catalog;
    private readonly MtaAudioWorker? worker;
    private readonly PolicySession? session;
    private int disposed;

    public WindowsDefaultOutputOperationPort(WindowsBluetoothAudioCatalogPort catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (WindowsSessionContext.IsRemoteSession)
        {
            Capability = DefaultOutputCapability.VerificationUnavailable;
            return;
        }

        MtaAudioWorker? createdWorker = null;
        try
        {
            createdWorker = new MtaAudioWorker();
            session = createdWorker.Invoke(() => new PolicySession());
            worker = createdWorker;
            Capability = DefaultOutputCapability.Supported;
        }
        catch
        {
            createdWorker?.Dispose();
            Capability = DefaultOutputCapability.PolicyUnavailable;
        }
    }

    public DefaultOutputCapability Capability { get; }

    public async ValueTask<DefaultOutputOperationResult> MakeDefaultAsync(
        BluetoothOperationTarget target,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Capability != DefaultOutputCapability.Supported ||
            worker is null ||
            session is null ||
            WindowsSessionContext.IsRemoteSession ||
            !catalog.TryResolveBinding(target, out WindowsBluetoothDeviceBinding? binding))
        {
            return Failed(BluetoothMutationFailure.OwnershipUnknown, submitted: false);
        }

        string? endpointId = worker.Invoke(() => session.ResolveActiveStereoRender(binding));
        if (endpointId is null || !catalog.TryResolveBinding(target, out _))
        {
            return Failed(BluetoothMutationFailure.DeviceUnavailable, submitted: false);
        }

        string? originalCommunications = worker.Invoke(
            () => session.GetDefaultEndpoint(AudioRole.Communications));
        bool submitted = false;
        foreach (AudioRole role in MutableRoles)
        {
            if (!catalog.TryResolveBinding(target, out _))
            {
                return Failed(BluetoothMutationFailure.OwnershipUnknown, submitted);
            }

            CancellationToken effectiveCancellation = submitted
                ? CancellationToken.None
                : cancellationToken;
            effectiveCancellation.ThrowIfCancellationRequested();
            string? current = worker.Invoke(() => session.GetDefaultEndpoint(role));
            if (string.Equals(current, endpointId, StringComparison.Ordinal))
            {
                continue;
            }

            PendingNotification pending = session.Arm(role, endpointId);
            try
            {
                int result = worker.Invoke(() => session.SetDefaultEndpoint(endpointId, role));
                submitted = true;
                if (result < 0)
                {
                    return Failed(BluetoothMutationFailure.Rejected, submitted: true);
                }

                bool notificationObserved = await WaitForNotificationAsync(pending)
                    .ConfigureAwait(false);
                string? readBack = worker.Invoke(() => session.GetDefaultEndpoint(role));
                if (!notificationObserved || !string.Equals(readBack, endpointId, StringComparison.Ordinal))
                {
                    return Failed(BluetoothMutationFailure.Faulted, submitted: true);
                }
            }
            finally
            {
                session.Disarm(pending.Id);
            }
        }

        string? currentCommunications = worker.Invoke(
            () => session.GetDefaultEndpoint(AudioRole.Communications));
        if (!string.Equals(
            originalCommunications,
            currentCommunications,
            StringComparison.Ordinal))
        {
            return Failed(BluetoothMutationFailure.Faulted, submitted);
        }

        if (!catalog.TryResolveBinding(target, out _))
        {
            return Failed(BluetoothMutationFailure.OwnershipUnknown, submitted);
        }

        return new(
            DefaultOutputState.Default,
            submitted,
            BluetoothMutationFailure.None);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (worker is not null && session is not null)
        {
            try
            {
                worker.Invoke(session.Dispose);
            }
            finally
            {
                worker.Dispose();
            }
        }
    }

    private static async Task<bool> WaitForNotificationAsync(PendingNotification pending)
    {
        try
        {
            await pending.Completion.Task.WaitAsync(NotificationTimeout, CancellationToken.None)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static DefaultOutputOperationResult Failed(
        BluetoothMutationFailure failure,
        bool submitted) =>
        new(DefaultOutputState.Failed, submitted, failure);

    private sealed class PolicySession : IDisposable
    {
        private readonly IMMDeviceEnumerator enumerator;
        private readonly IPolicyConfig policy;
        private readonly NotificationClient notificationClient;
        private readonly object notificationLock = new();
        private readonly Dictionary<long, PendingNotification> notifications = [];
        private long nextNotificationId;
        private bool registered;
        private int disposed;

        internal PolicySession()
        {
            IMMDeviceEnumerator? createdEnumerator = null;
            IPolicyConfig? createdPolicy = null;
            try
            {
                createdEnumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
                createdPolicy = (IPolicyConfig)(object)new PolicyConfigClientComObject();
                notificationClient = new NotificationClient(OnDefaultDeviceChanged);
                HResult.ThrowIfFailed(
                    createdEnumerator.RegisterEndpointNotificationCallback(notificationClient),
                    nameof(IMMDeviceEnumerator.RegisterEndpointNotificationCallback));
                registered = true;
                enumerator = createdEnumerator;
                policy = createdPolicy;
            }
            catch
            {
                ReleaseComObject(createdPolicy);
                ReleaseComObject(createdEnumerator);
                throw;
            }
        }

        internal string? ResolveActiveStereoRender(WindowsBluetoothDeviceBinding binding)
        {
            string[] candidates = [.. binding.Endpoints
                .Where(endpoint =>
                    endpoint.Direction == BluetoothEndpointDirection.Render &&
                    endpoint.Profile == BluetoothAudioProfile.Stereo &&
                    IsActive(endpoint.EndpointId))
                .Select(endpoint => endpoint.EndpointId)
                .Distinct(StringComparer.Ordinal)];
            return candidates.Length == 1 ? candidates[0] : null;
        }

        internal string? GetDefaultEndpoint(AudioRole role)
        {
            IMMDevice? device = null;
            try
            {
                int result = enumerator.GetDefaultAudioEndpoint(AudioDataFlow.Render, role, out device);
                if (result == unchecked((int)0x80070490))
                {
                    return null;
                }

                HResult.ThrowIfFailed(result, nameof(IMMDeviceEnumerator.GetDefaultAudioEndpoint));
                HResult.ThrowIfFailed(device.GetId(out string endpointId), nameof(IMMDevice.GetId));
                return endpointId;
            }
            finally
            {
                ReleaseComObject(device);
            }
        }

        internal int SetDefaultEndpoint(string endpointId, AudioRole role) =>
            policy.SetDefaultEndpoint(endpointId, role);

        internal PendingNotification Arm(AudioRole role, string endpointId)
        {
            var pending = new PendingNotification(
                checked(Interlocked.Increment(ref nextNotificationId)),
                role,
                endpointId);
            lock (notificationLock)
            {
                notifications.Add(pending.Id, pending);
            }

            return pending;
        }

        internal void Disarm(long id)
        {
            lock (notificationLock)
            {
                notifications.Remove(id);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            if (registered)
            {
                _ = enumerator.UnregisterEndpointNotificationCallback(notificationClient);
                registered = false;
            }

            lock (notificationLock)
            {
                foreach (PendingNotification pending in notifications.Values)
                {
                    pending.Completion.TrySetCanceled();
                }

                notifications.Clear();
            }

            ReleaseComObject(policy);
            ReleaseComObject(enumerator);
        }

        private bool IsActive(string endpointId)
        {
            IMMDevice? device = null;
            try
            {
                int result = enumerator.GetDevice(endpointId, out device);
                if (result < 0)
                {
                    return false;
                }

                return device.GetState(out AudioDeviceState state) >= 0 &&
                    (state & AudioDeviceState.Active) != 0;
            }
            finally
            {
                ReleaseComObject(device);
            }
        }

        private void OnDefaultDeviceChanged(
            AudioDataFlow flow,
            AudioRole role,
            string? endpointId)
        {
            if (flow != AudioDataFlow.Render || endpointId is null)
            {
                return;
            }

            lock (notificationLock)
            {
                foreach (PendingNotification pending in notifications.Values.Where(item =>
                    item.Role == role &&
                    string.Equals(item.EndpointId, endpointId, StringComparison.Ordinal)))
                {
                    pending.Completion.TrySetResult();
                }
            }
        }

        private static void ReleaseComObject(object? value)
        {
            if (value is not null && Marshal.IsComObject(value))
            {
                _ = Marshal.ReleaseComObject(value);
            }
        }
    }

    private sealed record PendingNotification(long Id, AudioRole Role, string EndpointId)
    {
        internal TaskCompletionSource Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class NotificationClient(
        Action<AudioDataFlow, AudioRole, string?> defaultChanged) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, AudioDeviceState newState) => 0;

        public int OnDeviceAdded(string deviceId) => 0;

        public int OnDeviceRemoved(string deviceId) => 0;

        public int OnDefaultDeviceChanged(
            AudioDataFlow dataFlow,
            AudioRole role,
            string? defaultDeviceId)
        {
            defaultChanged(dataFlow, role, defaultDeviceId);
            return 0;
        }

        public int OnPropertyValueChanged(string deviceId, PropertyKey propertyKey) => 0;
    }
}
