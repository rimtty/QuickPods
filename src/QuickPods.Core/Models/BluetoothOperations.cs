namespace QuickPods.Core.Models;

public enum BluetoothRequestedAction
{
    None,
    Connect,
    Disconnect,
}

public enum BluetoothOperationOutcome
{
    Idle,
    InProgress,
    Succeeded,
    ConnectedNotDefault,
    Unsupported,
    SelectionStale,
    Superseded,
    TimedOut,
    Rejected,
    ContainmentFailed,
    Faulted,
    Cancelled,
}

public enum BluetoothMutationFailure
{
    None,
    Unsupported,
    OwnershipUnknown,
    DeviceUnavailable,
    TimedOut,
    Rejected,
    ContainmentFailed,
    Faulted,
}

public readonly record struct BluetoothOperationTarget
{
    public BluetoothOperationTarget(
        BluetoothDeviceKey deviceKey,
        long inventoryGeneration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inventoryGeneration);
        DeviceKey = deviceKey;
        InventoryGeneration = inventoryGeneration;
    }

    public BluetoothDeviceKey DeviceKey { get; }

    public long InventoryGeneration { get; }
}

public sealed record BluetoothDeviceOperationResult(
    BluetoothConnectionState ConnectionState,
    bool RequestSubmitted,
    BluetoothMutationFailure Failure);

public sealed record DefaultOutputOperationResult(
    DefaultOutputState DefaultOutputState,
    bool RequestSubmitted,
    BluetoothMutationFailure Failure);

public sealed record BluetoothOperationSnapshot(
    long Revision,
    BluetoothOperationTarget? Target,
    BluetoothRequestedAction RequestedAction,
    QuickPodsOperation Operation,
    BluetoothOperationOutcome Outcome,
    BluetoothConnectionState ConnectionState,
    DefaultOutputState DefaultOutputState,
    QuickPodsErrorCode? Error)
{
    public static BluetoothOperationSnapshot Idle { get; } = new(
        0,
        null,
        BluetoothRequestedAction.None,
        QuickPodsOperation.None,
        BluetoothOperationOutcome.Idle,
        BluetoothConnectionState.Unknown,
        DefaultOutputState.NotApplicable,
        null);
}
