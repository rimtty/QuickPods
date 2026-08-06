using System.Runtime.InteropServices;
using QuickPods.Spike.DefaultEndpointPolicy.Interop;

namespace QuickPods.Spike.DefaultEndpointPolicy;

internal sealed class WindowsDefaultAudioEndpointPolicy : IDefaultAudioEndpointPolicy, IDisposable
{
    private readonly MtaComWorker _worker;
    private readonly PolicySession _session;
    private int _disposed;

    internal WindowsDefaultAudioEndpointPolicy()
    {
        _worker = new MtaComWorker();
        try
        {
            _session = _worker.Invoke(() => new PolicySession(_worker));
        }
        catch
        {
            _worker.Dispose();
            throw;
        }
    }

    public ValueTask<OpaqueEndpointHandle?> GetDefaultEndpointAsync(
        DefaultEndpointRole role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return ValueTask.FromResult(_worker.Invoke(() => _session.GetDefaultEndpoint(role)));
    }

    public ValueTask<DefaultEndpointWriteResult> SetDefaultEndpointAsync(
        OpaqueEndpointHandle endpoint,
        DefaultEndpointRole role,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        return ValueTask.FromResult(_worker.Invoke(() => _session.SetDefaultEndpoint(endpoint, role)));
    }

    public ValueTask<IDefaultEndpointNotificationSubscription> SubscribeDefaultEndpointChangedAsync(
        OpaqueEndpointHandle endpoint,
        DefaultEndpointRole role,
        long generation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        PendingNotification pending = _worker.Invoke(() =>
            _session.Subscribe(endpoint, role, generation));
        IDefaultEndpointNotificationSubscription subscription =
            new WindowsNotificationSubscription(this, pending);
        return ValueTask.FromResult(subscription);
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

    private void RemoveSubscription(long subscriptionId)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            _worker.Invoke(() => _session.RemoveSubscription(subscriptionId));
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed class PolicySession : IDisposable
    {
        private const int InvalidArgument = unchecked((int)0x80070057);

        private readonly MtaComWorker _worker;
        private readonly IMMDeviceEnumerator _enumerator;
        private readonly IPolicyConfig _policyConfig;
        private readonly PolicyNotificationClient _notificationClient;
        private readonly Dictionary<long, PendingNotification> _subscriptions = [];
        private long _nextSubscriptionId;
        private bool _registered;
        private int _disposed;

        internal PolicySession(MtaComWorker worker)
        {
            _worker = worker;
            IMMDeviceEnumerator? enumerator = null;
            IPolicyConfig? policyConfig = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
                policyConfig = (IPolicyConfig)(object)new PolicyConfigClientComObject();
                _notificationClient = new PolicyNotificationClient(
                    (flow, role, endpointId) =>
                        _worker.TryPost(() => OnDefaultEndpointChanged(flow, role, endpointId)));
                NativeCall.ThrowIfFailed(
                    enumerator.RegisterEndpointNotificationCallback(_notificationClient),
                    nameof(IMMDeviceEnumerator.RegisterEndpointNotificationCallback));
                _registered = true;
                _enumerator = enumerator;
                _policyConfig = policyConfig;
            }
            catch
            {
                ComObject.Release(policyConfig);
                ComObject.Release(enumerator);
                throw;
            }
        }

        internal OpaqueEndpointHandle? GetDefaultEndpoint(DefaultEndpointRole role)
        {
            IMMDevice? device = null;
            try
            {
                int result = _enumerator.GetDefaultAudioEndpoint(
                    NativeAudioDataFlow.Render,
                    MapRole(role),
                    out device);
                if (result == NativeAudioConstants.ElementNotFound)
                {
                    return null;
                }

                NativeCall.ThrowIfFailed(
                    result,
                    nameof(IMMDeviceEnumerator.GetDefaultAudioEndpoint));
                NativeCall.ThrowIfFailed(device.GetId(out string endpointId), nameof(IMMDevice.GetId));
                return new OpaqueEndpointHandle(endpointId);
            }
            finally
            {
                ComObject.Release(device);
            }
        }

        internal DefaultEndpointWriteResult SetDefaultEndpoint(
            OpaqueEndpointHandle endpoint,
            DefaultEndpointRole role)
        {
            if (role == DefaultEndpointRole.Communications)
            {
                return new DefaultEndpointWriteResult(Accepted: false, HResult: InvalidArgument);
            }

            IMMDevice? device = null;
            try
            {
                int getResult = _enumerator.GetDevice(endpoint.Value, out device);
                if (getResult < 0)
                {
                    return new DefaultEndpointWriteResult(Accepted: false, HResult: getResult);
                }

                int stateResult = device.GetState(out uint state);
                if (stateResult < 0)
                {
                    return new DefaultEndpointWriteResult(Accepted: false, HResult: stateResult);
                }

                if ((state & NativeAudioConstants.DeviceStateActive) == 0)
                {
                    return new DefaultEndpointWriteResult(
                        Accepted: false,
                        HResult: NativeAudioConstants.ElementNotFound);
                }

                int result = _policyConfig.SetDefaultEndpoint(endpoint.Value, MapRole(role));
                return new DefaultEndpointWriteResult(result >= 0, result);
            }
            catch (COMException exception)
            {
                return new DefaultEndpointWriteResult(Accepted: false, exception.HResult);
            }
            finally
            {
                ComObject.Release(device);
            }
        }

        internal PendingNotification Subscribe(
            OpaqueEndpointHandle endpoint,
            DefaultEndpointRole role,
            long generation)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            long id = checked(++_nextSubscriptionId);
            var pending = new PendingNotification(id, endpoint, role, generation);
            _subscriptions.Add(id, pending);
            return pending;
        }

        internal void RemoveSubscription(long subscriptionId)
        {
            _subscriptions.Remove(subscriptionId);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            foreach (PendingNotification pending in _subscriptions.Values)
            {
                pending.Completion.TrySetCanceled();
            }

            _subscriptions.Clear();
            if (_registered)
            {
                _ = _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
                _registered = false;
            }

            ComObject.Release(_policyConfig);
            ComObject.Release(_enumerator);
        }

        private void OnDefaultEndpointChanged(
            NativeAudioDataFlow flow,
            NativeAudioRole role,
            string? endpointId)
        {
            if (flow != NativeAudioDataFlow.Render || string.IsNullOrWhiteSpace(endpointId))
            {
                return;
            }

            DefaultEndpointRole mappedRole = MapRole(role);
            foreach (PendingNotification pending in _subscriptions.Values.Where(item =>
                item.Role == mappedRole &&
                StringComparer.Ordinal.Equals(item.Endpoint.Value, endpointId)))
            {
                pending.Completion.TrySetResult(new DefaultEndpointNotification(
                    pending.Role,
                    pending.Generation,
                    Observed: true));
            }
        }

        private static NativeAudioRole MapRole(DefaultEndpointRole role) => role switch
        {
            DefaultEndpointRole.Console => NativeAudioRole.Console,
            DefaultEndpointRole.Multimedia => NativeAudioRole.Multimedia,
            DefaultEndpointRole.Communications => NativeAudioRole.Communications,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

        private static DefaultEndpointRole MapRole(NativeAudioRole role) => role switch
        {
            NativeAudioRole.Console => DefaultEndpointRole.Console,
            NativeAudioRole.Multimedia => DefaultEndpointRole.Multimedia,
            NativeAudioRole.Communications => DefaultEndpointRole.Communications,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
    }

    private sealed class WindowsNotificationSubscription(
        WindowsDefaultAudioEndpointPolicy owner,
        PendingNotification pending) : IDefaultEndpointNotificationSubscription
    {
        private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(5);
        private int _disposed;

        public async ValueTask<DefaultEndpointNotification> WaitAsync(
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            try
            {
                return await pending.Completion.Task.WaitAsync(
                    NotificationTimeout,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return new DefaultEndpointNotification(
                    pending.Role,
                    pending.Generation,
                    Observed: false);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.RemoveSubscription(pending.Id);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed record PendingNotification(
        long Id,
        OpaqueEndpointHandle Endpoint,
        DefaultEndpointRole Role,
        long Generation)
    {
        internal TaskCompletionSource<DefaultEndpointNotification> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class PolicyNotificationClient(
        Func<NativeAudioDataFlow, NativeAudioRole, string?, bool> post)
        : IMMNotificationClient
    {
        private const int Fail = unchecked((int)0x80004005);

        public int OnDeviceStateChanged(string deviceId, uint newState) => 0;

        public int OnDeviceAdded(string deviceId) => 0;

        public int OnDeviceRemoved(string deviceId) => 0;

        public int OnDefaultDeviceChanged(
            NativeAudioDataFlow dataFlow,
            NativeAudioRole role,
            string? defaultDeviceId) => post(dataFlow, role, defaultDeviceId) ? 0 : Fail;

        public int OnPropertyValueChanged(string deviceId, PropertyKey propertyKey) => 0;
    }
}
