using QuickPods.Core.Models;
using QuickPods.Core.Ports;

namespace QuickPods.Core;

public sealed class BluetoothOperationController : IDisposable
{
    private readonly BluetoothCatalogController catalog;
    private readonly IBluetoothDeviceOperationPort bluetooth;
    private readonly IDefaultOutputOperationPort defaultOutput;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly object stateLock = new();
    private BluetoothOperationSnapshot state = BluetoothOperationSnapshot.Idle;
    private bool disposed;

    public BluetoothOperationController(
        BluetoothCatalogController catalog,
        IBluetoothDeviceOperationPort bluetooth,
        IDefaultOutputOperationPort defaultOutput)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.bluetooth = bluetooth ?? throw new ArgumentNullException(nameof(bluetooth));
        this.defaultOutput = defaultOutput ?? throw new ArgumentNullException(nameof(defaultOutput));
    }

    public event EventHandler<BluetoothOperationSnapshot>? StateChanged;

    public BluetoothOperationSnapshot State
    {
        get
        {
            lock (stateLock)
            {
                return state;
            }
        }
    }

    public ValueTask<BluetoothOperationSnapshot> ConnectSelectedAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(BluetoothRequestedAction.Connect, cancellationToken);

    public ValueTask<BluetoothOperationSnapshot> DisconnectSelectedAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(BluetoothRequestedAction.Disconnect, cancellationToken);

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            operationGate.Dispose();
        }
    }

    private async ValueTask<BluetoothOperationSnapshot> ExecuteAsync(
        BluetoothRequestedAction action,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BluetoothAudioCatalogSnapshot catalogState = catalog.State;
            BluetoothAudioDeviceDescriptor? selected = catalogState.Devices.FirstOrDefault(
                device => device.IsSelected);
            if (selected is null || catalogState.SelectedDeviceKey is null)
            {
                return PublishTerminal(
                    target: null,
                    action,
                    BluetoothOperationOutcome.SelectionStale,
                    BluetoothConnectionState.Unknown,
                    DefaultOutputState.NotApplicable,
                    QuickPodsErrorCode.BluetoothSelectionStale);
            }

            var target = new BluetoothOperationTarget(
                selected.DeviceKey,
                catalogState.InventoryGeneration);
            if (selected.Capability != BluetoothDeviceCapability.DirectControl)
            {
                return PublishTerminal(
                    target,
                    action,
                    BluetoothOperationOutcome.Unsupported,
                    selected.ConnectionState,
                    selected.DefaultOutputState,
                    QuickPodsErrorCode.BluetoothDriverUnsupported);
            }

            return action switch
            {
                BluetoothRequestedAction.Connect => await ConnectAsync(
                    target,
                    selected,
                    cancellationToken).ConfigureAwait(false),
                BluetoothRequestedAction.Disconnect => await DisconnectAsync(
                    target,
                    selected,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
        }
        finally
        {
            operationGate.Release();
        }
    }

    private async ValueTask<BluetoothOperationSnapshot> ConnectAsync(
        BluetoothOperationTarget target,
        BluetoothAudioDeviceDescriptor selected,
        CancellationToken cancellationToken)
    {
        Publish(
            target,
            BluetoothRequestedAction.Connect,
            QuickPodsOperation.Connecting,
            BluetoothOperationOutcome.InProgress,
            BluetoothConnectionState.Connecting,
            selected.DefaultOutputState,
            null);

        if (!IsCurrent(target))
        {
            return PublishSuperseded(
                target,
                BluetoothRequestedAction.Connect,
                selected.ConnectionState,
                selected.DefaultOutputState);
        }

        BluetoothDeviceOperationResult connection;
        try
        {
            connection = await bluetooth.ConnectAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PublishCancelled(target, BluetoothRequestedAction.Connect);
        }
        catch
        {
            return PublishFaulted(target, BluetoothRequestedAction.Connect);
        }

        if (!IsCurrent(target))
        {
            return PublishSuperseded(
                target,
                BluetoothRequestedAction.Connect,
                connection.ConnectionState,
                DefaultOutputState.NotApplicable);
        }

        if (connection.ConnectionState != BluetoothConnectionState.Connected ||
            connection.Failure != BluetoothMutationFailure.None)
        {
            return PublishMutationFailure(
                target,
                BluetoothRequestedAction.Connect,
                connection.ConnectionState,
                DefaultOutputState.NotApplicable,
                connection.Failure);
        }

        if (defaultOutput.Capability != DefaultOutputCapability.Supported)
        {
            return PublishTerminal(
                target,
                BluetoothRequestedAction.Connect,
                BluetoothOperationOutcome.ConnectedNotDefault,
                BluetoothConnectionState.Connected,
                DefaultOutputState.Failed,
                QuickPodsErrorCode.DefaultOutputSwitchFailed);
        }

        if (!IsCurrent(target))
        {
            return PublishSuperseded(
                target,
                BluetoothRequestedAction.Connect,
                BluetoothConnectionState.Connected,
                DefaultOutputState.NotDefault);
        }

        Publish(
            target,
            BluetoothRequestedAction.Connect,
            QuickPodsOperation.SettingDefault,
            BluetoothOperationOutcome.InProgress,
            BluetoothConnectionState.Connected,
            DefaultOutputState.SettingDefault,
            null);

        if (!IsCurrent(target))
        {
            return PublishSuperseded(
                target,
                BluetoothRequestedAction.Connect,
                BluetoothConnectionState.Connected,
                DefaultOutputState.NotDefault);
        }

        DefaultOutputOperationResult output;
        try
        {
            output = await defaultOutput.MakeDefaultAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PublishCancelled(
                target,
                BluetoothRequestedAction.Connect,
                BluetoothConnectionState.Connected,
                DefaultOutputState.Failed);
        }
        catch
        {
            return PublishFaulted(
                target,
                BluetoothRequestedAction.Connect,
                BluetoothConnectionState.Connected,
                DefaultOutputState.Failed);
        }

        if (!IsCurrent(target))
        {
            return PublishSuperseded(
                target,
                BluetoothRequestedAction.Connect,
                BluetoothConnectionState.Connected,
                output.DefaultOutputState);
        }

        if (output.DefaultOutputState != DefaultOutputState.Default ||
            output.Failure != BluetoothMutationFailure.None)
        {
            return PublishTerminal(
                target,
                BluetoothRequestedAction.Connect,
                BluetoothOperationOutcome.ConnectedNotDefault,
                BluetoothConnectionState.Connected,
                output.DefaultOutputState == DefaultOutputState.Default
                    ? DefaultOutputState.Failed
                    : output.DefaultOutputState,
                QuickPodsErrorCode.DefaultOutputSwitchFailed);
        }

        return PublishTerminal(
            target,
            BluetoothRequestedAction.Connect,
            BluetoothOperationOutcome.Succeeded,
            BluetoothConnectionState.Connected,
            DefaultOutputState.Default,
            null);
    }

    private async ValueTask<BluetoothOperationSnapshot> DisconnectAsync(
        BluetoothOperationTarget target,
        BluetoothAudioDeviceDescriptor selected,
        CancellationToken cancellationToken)
    {
        Publish(
            target,
            BluetoothRequestedAction.Disconnect,
            QuickPodsOperation.Disconnecting,
            BluetoothOperationOutcome.InProgress,
            BluetoothConnectionState.Disconnecting,
            selected.DefaultOutputState,
            null);

        if (!IsCurrent(target))
        {
            return PublishSuperseded(
                target,
                BluetoothRequestedAction.Disconnect,
                selected.ConnectionState,
                selected.DefaultOutputState);
        }

        BluetoothDeviceOperationResult result;
        try
        {
            result = await bluetooth.DisconnectAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PublishCancelled(target, BluetoothRequestedAction.Disconnect);
        }
        catch
        {
            return PublishFaulted(target, BluetoothRequestedAction.Disconnect);
        }

        if (!IsCurrent(target))
        {
            return PublishSuperseded(
                target,
                BluetoothRequestedAction.Disconnect,
                result.ConnectionState,
                selected.DefaultOutputState);
        }

        if (result.ConnectionState != BluetoothConnectionState.Disconnected ||
            result.Failure != BluetoothMutationFailure.None)
        {
            return PublishMutationFailure(
                target,
                BluetoothRequestedAction.Disconnect,
                result.ConnectionState,
                selected.DefaultOutputState,
                result.Failure);
        }

        return PublishTerminal(
            target,
            BluetoothRequestedAction.Disconnect,
            BluetoothOperationOutcome.Succeeded,
            BluetoothConnectionState.Disconnected,
            DefaultOutputState.NotApplicable,
            null);
    }

    private bool IsCurrent(BluetoothOperationTarget target)
    {
        BluetoothAudioCatalogSnapshot current = catalog.State;
        return current.InventoryGeneration == target.InventoryGeneration &&
            current.SelectedDeviceKey == target.DeviceKey &&
            current.SelectedDevicePresent;
    }

    private BluetoothOperationSnapshot PublishMutationFailure(
        BluetoothOperationTarget target,
        BluetoothRequestedAction action,
        BluetoothConnectionState connection,
        DefaultOutputState output,
        BluetoothMutationFailure failure) =>
        failure switch
        {
            BluetoothMutationFailure.Unsupported or BluetoothMutationFailure.OwnershipUnknown =>
                PublishTerminal(target, action, BluetoothOperationOutcome.Unsupported, connection, output,
                    QuickPodsErrorCode.BluetoothDriverUnsupported),
            BluetoothMutationFailure.DeviceUnavailable =>
                PublishTerminal(target, action, BluetoothOperationOutcome.SelectionStale, connection, output,
                    QuickPodsErrorCode.BluetoothDeviceUnavailable),
            BluetoothMutationFailure.TimedOut =>
                PublishTerminal(target, action, BluetoothOperationOutcome.TimedOut, connection, output,
                    QuickPodsErrorCode.BluetoothTimeout),
            BluetoothMutationFailure.Rejected =>
                PublishTerminal(target, action, BluetoothOperationOutcome.Rejected, connection, output,
                    QuickPodsErrorCode.BluetoothOperationRejected),
            BluetoothMutationFailure.ContainmentFailed =>
                PublishTerminal(target, action, BluetoothOperationOutcome.ContainmentFailed, connection, output,
                    QuickPodsErrorCode.BluetoothContainmentFailed),
            _ => PublishTerminal(target, action, BluetoothOperationOutcome.Faulted, connection, output,
                QuickPodsErrorCode.BluetoothOperationRejected),
        };

    private BluetoothOperationSnapshot PublishCancelled(
        BluetoothOperationTarget target,
        BluetoothRequestedAction action,
        BluetoothConnectionState connection = BluetoothConnectionState.Unknown,
        DefaultOutputState output = DefaultOutputState.NotApplicable) =>
        PublishTerminal(
            target,
            action,
            BluetoothOperationOutcome.Cancelled,
            connection,
            output,
            null);

    private BluetoothOperationSnapshot PublishFaulted(
        BluetoothOperationTarget target,
        BluetoothRequestedAction action,
        BluetoothConnectionState connection = BluetoothConnectionState.Unknown,
        DefaultOutputState output = DefaultOutputState.NotApplicable) =>
        PublishTerminal(
            target,
            action,
            BluetoothOperationOutcome.Faulted,
            connection,
            output,
            QuickPodsErrorCode.BluetoothOperationRejected);

    private BluetoothOperationSnapshot PublishSuperseded(
        BluetoothOperationTarget target,
        BluetoothRequestedAction action,
        BluetoothConnectionState connection,
        DefaultOutputState output) =>
        PublishTerminal(
            target,
            action,
            BluetoothOperationOutcome.Superseded,
            connection,
            output,
            QuickPodsErrorCode.BluetoothSelectionStale);

    private BluetoothOperationSnapshot PublishTerminal(
        BluetoothOperationTarget? target,
        BluetoothRequestedAction action,
        BluetoothOperationOutcome outcome,
        BluetoothConnectionState connection,
        DefaultOutputState output,
        QuickPodsErrorCode? error) =>
        Publish(
            target,
            action,
            QuickPodsOperation.None,
            outcome,
            connection,
            output,
            error);

    private BluetoothOperationSnapshot Publish(
        BluetoothOperationTarget? target,
        BluetoothRequestedAction action,
        QuickPodsOperation operation,
        BluetoothOperationOutcome outcome,
        BluetoothConnectionState connection,
        DefaultOutputState output,
        QuickPodsErrorCode? error)
    {
        BluetoothOperationSnapshot published;
        lock (stateLock)
        {
            published = new(
                checked(state.Revision + 1),
                target,
                action,
                operation,
                outcome,
                connection,
                output,
                error);
            state = published;
        }

        StateChanged?.Invoke(this, published);
        return published;
    }
}
