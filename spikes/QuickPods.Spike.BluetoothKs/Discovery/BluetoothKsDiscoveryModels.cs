using QuickPods.Spike.BluetoothKs.Interop;

namespace QuickPods.Spike.BluetoothKs.Discovery;

internal interface IBluetoothKsDiscovery
{
    BluetoothKsDiscoveryResult Discover();
}

internal sealed record SanitizedAudioEndpoint(
    string EndpointHash,
    string Label,
    NativeDataFlow Flow,
    uint State);

internal sealed record SanitizedKsFilterCandidate(
    string FilterHash,
    string SourceEndpointHash,
    NativeDataFlow SourceFlow,
    string ProbeStatus);

internal sealed record BluetoothDeviceGroup(
    string ContainerHash,
    string Label,
    IReadOnlyList<SanitizedAudioEndpoint> Endpoints,
    IReadOnlyList<SanitizedKsFilterCandidate> FilterCandidates);

internal sealed record DiscoveryFault(
    string Operation,
    int HResult,
    bool AffectsOwnership = true);

internal sealed record BluetoothKsInventory(
    string SessionToken,
    IReadOnlyList<BluetoothDeviceGroup> Groups,
    IReadOnlyList<DiscoveryFault> Faults);

internal sealed record RawKsTarget(
    string ContainerHash,
    IReadOnlyList<RawKsCandidate> Candidates);

internal sealed record RawKsCandidate(
    string AdapterDeviceId,
    NativeDataFlow SourceFlow);

internal sealed record BluetoothKsDiscoveryResult(
    BluetoothKsInventory Inventory,
    IReadOnlyDictionary<string, RawKsTarget> Targets,
    IReadOnlyList<string> UnassignedAdapterDeviceIds);
