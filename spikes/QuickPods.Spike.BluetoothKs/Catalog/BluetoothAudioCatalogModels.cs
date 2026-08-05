using QuickPods.Spike.BluetoothKs.Interop;
using QuickPods.Spike.BluetoothKs.Observation;

namespace QuickPods.Spike.BluetoothKs.Catalog;

internal enum BluetoothAudioProfile
{
    Stereo,
    HandsFree,
    Other,
}

internal enum BluetoothAudioDeviceKind
{
    Unknown,
    Earbuds,
    Headphones,
    Headset,
    Speaker,
}

internal enum BluetoothCatalogConnectionState
{
    Connected,
    Disconnected,
    Unavailable,
    Unknown,
}

internal sealed record BluetoothAudioEndpointCandidate(
    string ContainerKey,
    string EndpointKey,
    string DisplayName,
    BluetoothAudioDeviceKind Kind,
    BluetoothAudioProfile Profile,
    NativeDataFlow Flow,
    BluetoothEndpointState State,
    bool IsPaired,
    bool IsBluetooth);

internal sealed record BluetoothAudioCatalogDevice(
    string ContainerKey,
    string DisplayName,
    BluetoothAudioDeviceKind Kind,
    BluetoothCatalogConnectionState ConnectionState,
    IReadOnlyList<BluetoothAudioProfile> Profiles,
    IReadOnlyList<string> EndpointKeys);

internal sealed record BluetoothAudioCatalogSnapshot(
    long Revision,
    string? SelectedContainerKey,
    bool SelectedDevicePresent,
    IReadOnlyList<BluetoothAudioCatalogDevice> Devices);
