using System.Collections.Immutable;

namespace QuickPods.Core.Models;

public enum BluetoothAudioKind
{
    Unknown,
    Earbuds,
    Headphones,
    Headset,
    Speaker,
}

public enum BluetoothAudioProfile
{
    Stereo,
    HandsFree,
    Other,
}

public enum BluetoothEndpointDirection
{
    Render,
    Capture,
}

public enum BluetoothEndpointAvailability
{
    Active,
    Disabled,
    NotPresent,
    Unplugged,
    Unknown,
}

public sealed record BluetoothAudioEndpointEvidence(
    BluetoothDeviceKey DeviceKey,
    string DisplayName,
    BluetoothAudioKind Kind,
    BluetoothAudioProfile Profile,
    BluetoothEndpointDirection Direction,
    BluetoothEndpointAvailability Availability,
    BluetoothDeviceCapability Capability,
    bool IsPaired,
    bool IsBluetooth)
{
    public ImmutableArray<byte> IconPng { get; init; } = [];

    public bool IsConsoleDefault { get; init; }

    public bool IsMultimediaDefault { get; init; }
}

public sealed record BluetoothAudioDeviceDescriptor(
    BluetoothDeviceKey DeviceKey,
    string DisplayName,
    BluetoothAudioKind Kind,
    BluetoothConnectionState ConnectionState,
    DefaultOutputState DefaultOutputState,
    BluetoothDeviceCapability Capability,
    ImmutableArray<BluetoothAudioProfile> Profiles,
    bool IsSelected)
{
    public ImmutableArray<byte> IconPng { get; init; } = [];
}

public sealed record BluetoothAudioCatalogSnapshot(
    long InventoryGeneration,
    long Revision,
    BluetoothDeviceKey? SelectedDeviceKey,
    bool SelectedDevicePresent,
    ImmutableArray<BluetoothAudioDeviceDescriptor> Devices)
{
    public static BluetoothAudioCatalogSnapshot Empty { get; } = new(
        0,
        0,
        null,
        false,
        []);
}

public sealed record BluetoothAudioCatalogObservation
{
    public BluetoothAudioCatalogObservation(
        long inventoryGeneration,
        IEnumerable<BluetoothAudioEndpointEvidence> endpoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inventoryGeneration);
        ArgumentNullException.ThrowIfNull(endpoints);
        InventoryGeneration = inventoryGeneration;
        Endpoints = [.. endpoints];
    }

    public long InventoryGeneration { get; }

    public ImmutableArray<BluetoothAudioEndpointEvidence> Endpoints { get; }
}
