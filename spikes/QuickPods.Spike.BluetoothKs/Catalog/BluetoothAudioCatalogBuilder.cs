using QuickPods.Spike.BluetoothKs.Interop;
using QuickPods.Spike.BluetoothKs.Observation;

namespace QuickPods.Spike.BluetoothKs.Catalog;

internal static class BluetoothAudioCatalogBuilder
{
    public static IReadOnlyList<BluetoothAudioCatalogDevice> Build(
        IEnumerable<BluetoothAudioEndpointCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return [.. candidates
            .Where(IsCatalogCandidate)
            .GroupBy(candidate => candidate.ContainerKey, StringComparer.Ordinal)
            .Select(BuildDevice)
            .OrderBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.ContainerKey, StringComparer.Ordinal)];
    }

    private static bool IsCatalogCandidate(BluetoothAudioEndpointCandidate candidate)
    {
        return candidate.IsBluetooth &&
            candidate.IsPaired &&
            !string.IsNullOrWhiteSpace(candidate.ContainerKey) &&
            !string.IsNullOrWhiteSpace(candidate.EndpointKey);
    }

    private static BluetoothAudioCatalogDevice BuildDevice(
        IGrouping<string, BluetoothAudioEndpointCandidate> group)
    {
        BluetoothAudioEndpointCandidate[] endpoints = [.. group
            .OrderBy(endpoint => endpoint.EndpointKey, StringComparer.Ordinal)];
        string displayName = endpoints
            .Select(endpoint => endpoint.DisplayName.Trim())
            .FirstOrDefault(name => name.Length > 0) ?? "Bluetooth audio";
        BluetoothAudioDeviceKind kind = ResolveKind(endpoints);
        BluetoothCatalogConnectionState connection = ResolveConnection(endpoints);
        BluetoothAudioProfile[] profiles = [.. endpoints
            .Select(endpoint => endpoint.Profile)
            .Distinct()
            .OrderBy(profile => profile)];
        string[] endpointKeys = [.. endpoints
            .Select(endpoint => endpoint.EndpointKey)
            .Distinct(StringComparer.Ordinal)];

        return new BluetoothAudioCatalogDevice(
            group.Key,
            displayName,
            kind,
            connection,
            profiles,
            endpointKeys);
    }

    private static BluetoothAudioDeviceKind ResolveKind(
        IReadOnlyList<BluetoothAudioEndpointCandidate> endpoints)
    {
        BluetoothAudioDeviceKind[] kinds = [.. endpoints
            .Select(endpoint => endpoint.Kind)
            .Where(kind => kind != BluetoothAudioDeviceKind.Unknown)
            .Distinct()];
        return kinds.Length == 1 ? kinds[0] : BluetoothAudioDeviceKind.Unknown;
    }

    private static BluetoothCatalogConnectionState ResolveConnection(
        IReadOnlyList<BluetoothAudioEndpointCandidate> endpoints)
    {
        BluetoothEndpointState[] renderStates = [.. endpoints
            .Where(endpoint => endpoint.Flow == NativeDataFlow.Render)
            .Select(endpoint => endpoint.State)];
        if (renderStates.Contains(BluetoothEndpointState.Active))
        {
            return BluetoothCatalogConnectionState.Connected;
        }

        if (renderStates.Length == 0 ||
            renderStates.All(state => state == BluetoothEndpointState.Disabled))
        {
            return BluetoothCatalogConnectionState.Unavailable;
        }

        if (renderStates.All(state =>
            state is BluetoothEndpointState.NotPresent or BluetoothEndpointState.Unplugged))
        {
            return BluetoothCatalogConnectionState.Disconnected;
        }

        return BluetoothCatalogConnectionState.Unknown;
    }
}
