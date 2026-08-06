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
    bool AffectsOwnership = true,
    string? EndpointHash = null,
    string? ContainerHash = null)
{
    public bool IsGlobal => EndpointHash is null && ContainerHash is null;
}

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

internal sealed record TargetOwnershipStatus(
    bool HasRelevantFault,
    bool HasUnassignedCandidateOverlap)
{
    public bool IsComplete => !HasRelevantFault && !HasUnassignedCandidateOverlap;
}

internal static class BluetoothKsOwnershipVerifier
{
    public static TargetOwnershipStatus Evaluate(
        BluetoothKsDiscoveryResult result,
        string targetContainerHash)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetContainerHash);

        BluetoothDeviceGroup? group = result.Inventory.Groups.SingleOrDefault(candidate =>
            string.Equals(
                candidate.ContainerHash,
                targetContainerHash,
                StringComparison.Ordinal));
        var targetEndpointHashes = new HashSet<string>(
            group?.Endpoints.Select(endpoint => endpoint.EndpointHash) ?? [],
            StringComparer.Ordinal);
        bool hasRelevantFault = result.Inventory.Faults.Any(fault =>
            FaultAffectsTarget(fault, targetContainerHash, targetEndpointHashes));

        bool hasUnassignedCandidateOverlap =
            result.Targets.TryGetValue(targetContainerHash, out RawKsTarget? target) &&
            target.Candidates.Any(candidate => result.UnassignedAdapterDeviceIds.Contains(
                candidate.AdapterDeviceId,
                StringComparer.Ordinal));

        return new TargetOwnershipStatus(
            hasRelevantFault,
            hasUnassignedCandidateOverlap);
    }

    private static bool FaultAffectsTarget(
        DiscoveryFault fault,
        string targetContainerHash,
        HashSet<string> targetEndpointHashes)
    {
        if (!fault.AffectsOwnership)
        {
            return false;
        }

        if (fault.IsGlobal)
        {
            return true;
        }

        return fault.ContainerHash is not null &&
                string.Equals(
                    fault.ContainerHash,
                    targetContainerHash,
                    StringComparison.Ordinal) ||
            fault.EndpointHash is not null &&
                targetEndpointHashes.Contains(fault.EndpointHash);
    }
}
