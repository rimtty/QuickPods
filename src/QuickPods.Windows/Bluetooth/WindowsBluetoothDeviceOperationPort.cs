using System.Diagnostics;
using System.Runtime.InteropServices;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Windows.Audio.Interop;
using QuickPods.Windows.Bluetooth.Worker;

namespace QuickPods.Windows.Bluetooth;

public sealed class WindowsBluetoothDeviceOperationPort : IBluetoothDeviceOperationPort, IDisposable
{
    private const int ElementNotFound = unchecked((int)0x80070490);
    private static readonly TimeSpan OperationDeadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DisconnectedStableWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ObservationInterval = TimeSpan.FromMilliseconds(250);

    private readonly WindowsBluetoothAudioCatalogPort catalog;
    private readonly WindowsBluetoothCapabilityProbe capabilityProbe;
    private readonly BluetoothWorkerProcessRunner worker;
    private readonly MtaAudioWorker audioWorker = new();
    private int disposed;

    public WindowsBluetoothDeviceOperationPort(WindowsBluetoothAudioCatalogPort catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        string workerPath = Path.Combine(AppContext.BaseDirectory, "QuickPods.BluetoothWorker.exe");
        capabilityProbe = new WindowsBluetoothCapabilityProbe(workerPath);
        worker = new BluetoothWorkerProcessRunner(workerPath);
    }

    public ValueTask<BluetoothDeviceOperationResult> ConnectAsync(
        BluetoothOperationTarget target,
        CancellationToken cancellationToken) => ExecuteConnectAsync(target, cancellationToken);

    public ValueTask<BluetoothDeviceOperationResult> DisconnectAsync(
        BluetoothOperationTarget target,
        CancellationToken cancellationToken) => ExecuteDisconnectAsync(target, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            audioWorker.Dispose();
        }
    }

    private async ValueTask<BluetoothDeviceOperationResult> ExecuteConnectAsync(
        BluetoothOperationTarget target,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (WindowsSessionContext.IsRemoteSession ||
            !catalog.TryResolveBinding(target, out WindowsBluetoothDeviceBinding? binding))
        {
            return Failure(
                BluetoothConnectionState.Unknown,
                BluetoothMutationFailure.OwnershipUnknown);
        }

        EndpointObservation preflight = await ObserveOnceAsync(binding).ConfigureAwait(false);
        if (preflight.RenderActive)
        {
            return new(
                BluetoothConnectionState.Connected,
                RequestSubmitted: false,
                BluetoothMutationFailure.None);
        }

        BluetoothDeviceCapability capability = await capabilityProbe.ProbeAsync(
            binding,
            cancellationToken).ConfigureAwait(false);
        if (capability != BluetoothDeviceCapability.DirectControl)
        {
            return Failure(preflight.ConnectionState, MapCapabilityFailure(capability));
        }

        string? reconnectAdapter = ResolveReconnectAdapter(binding);
        if (reconnectAdapter is null || !catalog.TryResolveBinding(target, out _))
        {
            return Failure(preflight.ConnectionState, BluetoothMutationFailure.OwnershipUnknown);
        }

        BluetoothMutationFailure requestFailure = await RunMutationAsync(
            target.DeviceKey,
            reconnectAdapter,
            BluetoothWorkerOperation.Reconnect,
            cancellationToken).ConfigureAwait(false);
        if (requestFailure != BluetoothMutationFailure.None)
        {
            return Failure(preflight.ConnectionState, requestFailure, submitted: true);
        }

        return await ObserveConnectedAsync(binding).ConfigureAwait(false);
    }

    private async ValueTask<BluetoothDeviceOperationResult> ExecuteDisconnectAsync(
        BluetoothOperationTarget target,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (WindowsSessionContext.IsRemoteSession ||
            !catalog.TryResolveBinding(target, out WindowsBluetoothDeviceBinding? binding))
        {
            return Failure(
                BluetoothConnectionState.Unknown,
                BluetoothMutationFailure.OwnershipUnknown);
        }

        EndpointObservation preflight = await ObserveOnceAsync(binding).ConfigureAwait(false);
        if (preflight.AllDisconnected)
        {
            return new(
                BluetoothConnectionState.Disconnected,
                RequestSubmitted: false,
                BluetoothMutationFailure.None);
        }

        BluetoothDeviceCapability capability = await capabilityProbe.ProbeAsync(
            binding,
            cancellationToken).ConfigureAwait(false);
        if (capability != BluetoothDeviceCapability.DirectControl)
        {
            return Failure(preflight.ConnectionState, MapCapabilityFailure(capability));
        }

        string[] disconnectAdapters = ResolveDisconnectAdapters(binding);
        if (disconnectAdapters.Length == 0 || !catalog.TryResolveBinding(target, out _))
        {
            return Failure(preflight.ConnectionState, BluetoothMutationFailure.OwnershipUnknown);
        }

        bool submitted = false;
        foreach (string adapter in disconnectAdapters)
        {
            if (!catalog.TryResolveBinding(target, out _))
            {
                EndpointObservation staleObservation = await ObserveOnceAsync(binding)
                    .ConfigureAwait(false);
                return Failure(
                    staleObservation.ConnectionState,
                    BluetoothMutationFailure.OwnershipUnknown,
                    submitted);
            }

            BluetoothMutationFailure requestFailure = await RunMutationAsync(
                target.DeviceKey,
                adapter,
                BluetoothWorkerOperation.Disconnect,
                submitted ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
            submitted = true;
            if (requestFailure != BluetoothMutationFailure.None)
            {
                return Failure(preflight.ConnectionState, requestFailure, submitted: true);
            }
        }

        return await ObserveDisconnectedAsync(binding).ConfigureAwait(false);
    }

    private async Task<BluetoothDeviceOperationResult> ObserveConnectedAsync(
        WindowsBluetoothDeviceBinding binding)
    {
        var stopwatch = Stopwatch.StartNew();
        EndpointObservation latest = EndpointObservation.Unknown;
        while (stopwatch.Elapsed < OperationDeadline)
        {
            latest = await ObserveOnceAsync(binding).ConfigureAwait(false);
            if (latest.RenderActive)
            {
                return new(
                    BluetoothConnectionState.Connected,
                    RequestSubmitted: true,
                    BluetoothMutationFailure.None);
            }

            await Task.Delay(ObservationInterval, CancellationToken.None).ConfigureAwait(false);
        }

        return Failure(latest.ConnectionState, BluetoothMutationFailure.TimedOut, submitted: true);
    }

    private async Task<BluetoothDeviceOperationResult> ObserveDisconnectedAsync(
        WindowsBluetoothDeviceBinding binding)
    {
        var stopwatch = Stopwatch.StartNew();
        TimeSpan? stableSince = null;
        EndpointObservation latest = EndpointObservation.Unknown;
        while (stopwatch.Elapsed < OperationDeadline)
        {
            latest = await ObserveOnceAsync(binding).ConfigureAwait(false);
            if (latest.AllDisconnected)
            {
                stableSince ??= stopwatch.Elapsed;
                if (stopwatch.Elapsed - stableSince >= DisconnectedStableWindow)
                {
                    return new(
                        BluetoothConnectionState.Disconnected,
                        RequestSubmitted: true,
                        BluetoothMutationFailure.None);
                }
            }
            else
            {
                stableSince = null;
            }

            await Task.Delay(ObservationInterval, CancellationToken.None).ConfigureAwait(false);
        }

        return Failure(latest.ConnectionState, BluetoothMutationFailure.TimedOut, submitted: true);
    }

    private ValueTask<EndpointObservation> ObserveOnceAsync(WindowsBluetoothDeviceBinding binding) =>
        audioWorker.InvokeAsync(
            () => ReadEndpointStates(binding),
            CancellationToken.None);

    private static EndpointObservation ReadEndpointStates(WindowsBluetoothDeviceBinding binding)
    {
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            var states = new List<(BluetoothEndpointDirection Direction, BluetoothEndpointAvailability State)>();
            foreach (WindowsBluetoothEndpointBinding endpoint in binding.Endpoints)
            {
                IMMDevice? device = null;
                try
                {
                    int result = enumerator.GetDevice(endpoint.EndpointId, out device);
                    if (result < 0)
                    {
                        states.Add((
                            endpoint.Direction,
                            result == ElementNotFound
                                ? BluetoothEndpointAvailability.NotPresent
                                : BluetoothEndpointAvailability.Unknown));
                        continue;
                    }

                    HResult.ThrowIfFailed(device.GetState(out AudioDeviceState state), nameof(IMMDevice.GetState));
                    states.Add((endpoint.Direction, MapAvailability(state)));
                }
                catch (Exception exception) when (
                    exception is COMException or InvalidCastException or CoreAudioInteropException)
                {
                    states.Add((endpoint.Direction, BluetoothEndpointAvailability.Unknown));
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }

            bool renderActive = states.Any(state =>
                state.Direction == BluetoothEndpointDirection.Render &&
                state.State == BluetoothEndpointAvailability.Active);
            bool allDisconnected = states.Count > 0 && states.All(state => state.State is
                BluetoothEndpointAvailability.NotPresent or BluetoothEndpointAvailability.Unplugged);
            BluetoothConnectionState connection = renderActive
                ? BluetoothConnectionState.Connected
                : allDisconnected
                    ? BluetoothConnectionState.Disconnected
                    : states.Count > 0 && states.All(state =>
                        state.State == BluetoothEndpointAvailability.Disabled)
                        ? BluetoothConnectionState.Unavailable
                        : BluetoothConnectionState.Unknown;
            return new(renderActive, allDisconnected, connection);
        }
        finally
        {
            ReleaseComObject(enumerator);
        }
    }

    private async Task<BluetoothMutationFailure> RunMutationAsync(
        BluetoothDeviceKey targetKey,
        string adapterDeviceId,
        BluetoothWorkerOperation operation,
        CancellationToken cancellationToken)
    {
        BluetoothWorkerRunResult result;
        try
        {
            result = await worker.RunAsync(
                targetKey,
                adapterDeviceId,
                operation,
                mutationConfirmed: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return BluetoothMutationFailure.Faulted;
        }

        return result.Status switch
        {
            BluetoothWorkerRunStatus.TimedOut => BluetoothMutationFailure.TimedOut,
            BluetoothWorkerRunStatus.ContainmentFailed => BluetoothMutationFailure.ContainmentFailed,
            BluetoothWorkerRunStatus.Faulted => BluetoothMutationFailure.Faulted,
            BluetoothWorkerRunStatus.Completed when result.Response?.HResult == 0 =>
                BluetoothMutationFailure.None,
            _ => BluetoothMutationFailure.Rejected,
        };
    }

    private static string? ResolveReconnectAdapter(WindowsBluetoothDeviceBinding binding)
    {
        string[] candidates = [.. binding.Endpoints
            .Where(endpoint =>
                endpoint.Direction == BluetoothEndpointDirection.Render &&
                endpoint.Profile == BluetoothAudioProfile.Stereo)
            .SelectMany(endpoint => endpoint.AdapterDeviceIds)
            .Distinct(StringComparer.Ordinal)];
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static string[] ResolveDisconnectAdapters(WindowsBluetoothDeviceBinding binding) =>
        [.. binding.Endpoints
            .SelectMany(endpoint => endpoint.AdapterDeviceIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static BluetoothMutationFailure MapCapabilityFailure(
        BluetoothDeviceCapability capability) =>
        capability switch
        {
            BluetoothDeviceCapability.SettingsOnly => BluetoothMutationFailure.Unsupported,
            BluetoothDeviceCapability.OwnershipUnknown => BluetoothMutationFailure.OwnershipUnknown,
            _ => BluetoothMutationFailure.DeviceUnavailable,
        };

    private static BluetoothDeviceOperationResult Failure(
        BluetoothConnectionState state,
        BluetoothMutationFailure failure,
        bool submitted = false) =>
        new(state, submitted, failure);

    private static BluetoothEndpointAvailability MapAvailability(AudioDeviceState state)
    {
        if ((state & AudioDeviceState.Active) != 0)
        {
            return BluetoothEndpointAvailability.Active;
        }

        if ((state & AudioDeviceState.Disabled) != 0)
        {
            return BluetoothEndpointAvailability.Disabled;
        }

        if ((state & AudioDeviceState.NotPresent) != 0)
        {
            return BluetoothEndpointAvailability.NotPresent;
        }

        return (state & AudioDeviceState.Unplugged) != 0
            ? BluetoothEndpointAvailability.Unplugged
            : BluetoothEndpointAvailability.Unknown;
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.ReleaseComObject(value);
        }
    }

    private sealed record EndpointObservation(
        bool RenderActive,
        bool AllDisconnected,
        BluetoothConnectionState ConnectionState)
    {
        internal static EndpointObservation Unknown { get; } = new(
            false,
            false,
            BluetoothConnectionState.Unknown);
    }
}
