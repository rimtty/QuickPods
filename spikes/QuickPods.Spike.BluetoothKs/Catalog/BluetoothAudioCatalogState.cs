namespace QuickPods.Spike.BluetoothKs.Catalog;

internal sealed class BluetoothAudioCatalogState(string? persistedSelection = null)
{
    private IReadOnlyList<BluetoothAudioCatalogDevice> _devices = [];
    private string? _selectedContainerKey = Normalize(persistedSelection);
    private long _revision;

    public BluetoothAudioCatalogSnapshot Refresh(
        IEnumerable<BluetoothAudioEndpointCandidate> candidates)
    {
        _devices = BluetoothAudioCatalogBuilder.Build(candidates);
        return CreateSnapshot(checked(++_revision));
    }

    public BluetoothAudioCatalogSnapshot Select(string containerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerKey);
        if (!_devices.Any(device => string.Equals(
            device.ContainerKey,
            containerKey,
            StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "The selected Bluetooth audio device is not present in the current catalog.",
                nameof(containerKey));
        }

        _selectedContainerKey = containerKey;
        return CreateSnapshot(checked(++_revision));
    }

    private BluetoothAudioCatalogSnapshot CreateSnapshot(long revision)
    {
        bool selectedDevicePresent = _selectedContainerKey is not null &&
            _devices.Any(device => string.Equals(
                device.ContainerKey,
                _selectedContainerKey,
                StringComparison.Ordinal));
        BluetoothAudioCatalogDevice[] orderedDevices = [.. _devices
            .OrderByDescending(device => string.Equals(
                device.ContainerKey,
                _selectedContainerKey,
                StringComparison.Ordinal))
            .ThenByDescending(device =>
                device.ConnectionState == BluetoothCatalogConnectionState.Connected)
            .ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.ContainerKey, StringComparer.Ordinal)];
        return new BluetoothAudioCatalogSnapshot(
            revision,
            _selectedContainerKey,
            selectedDevicePresent,
            orderedDevices);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
