using QuickPods.Spike.BluetoothKs.Observation;

namespace QuickPods.Spike.BluetoothKs.Operations;

public enum BluetoothOperationKind
{
    Connect,
    Disconnect,
}

public enum BluetoothOperationOutcome
{
    Succeeded,
    AlreadyInDesiredState,
    NotConfigured,
    Disabled,
    Unknown,
    KsRequestRejected,
    KsWatchdogTimedOut,
    DeadlineExceeded,
    Superseded,
    Faulted,
}

public sealed record BluetoothOperationRequest(
    string ContainerKey,
    BluetoothOperationKind Kind,
    long Generation,
    long StartedTimestamp);

public sealed record BluetoothOperationResult(
    BluetoothOperationRequest Request,
    BluetoothOperationOutcome Outcome,
    BluetoothAudioState ActualState,
    KsCallWatchdogStatus KsCallStatus,
    int? KsHResult,
    bool KsRequestIssued,
    TimeSpan Elapsed)
{
    public bool Succeeded => Outcome == BluetoothOperationOutcome.Succeeded;
}

public interface IBluetoothKsCommandInvoker
{
    Task<int> InvokeAsync(BluetoothOperationRequest request);
}

public sealed class BluetoothKsInvocationTimedOutException : TimeoutException
{
    public BluetoothKsInvocationTimedOutException()
        : base("The isolated Bluetooth KS invocation reached its containment deadline.")
    {
    }
}
