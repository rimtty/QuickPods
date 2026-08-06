namespace QuickPods.Spike.BluetoothKs.Observation;

public enum BluetoothAudioState
{
    Connected,
    Disconnected,
    Disabled,
    NotConfigured,
    Unknown,
}

public enum BluetoothEndpointState
{
    Active,
    Disabled,
    NotPresent,
    Unplugged,
    Unknown,
}

public sealed record BluetoothContainerEvidence(
    bool TargetConfigured,
    bool ContainerPresent,
    bool EnumerationComplete,
    IReadOnlyList<BluetoothEndpointState> EndpointStates);

public sealed record BluetoothStateObservation(
    long Generation,
    BluetoothContainerEvidence Evidence);

public interface IBluetoothStateObserver
{
    Task<BluetoothStateObservation> ObserveAsync(
        string containerKey,
        long generation,
        CancellationToken cancellationToken);
}
