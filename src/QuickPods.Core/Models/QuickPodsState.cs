using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace QuickPods.Core.Models;

public enum BluetoothConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Unavailable,
    Unknown,
}

public enum DefaultOutputState
{
    NotApplicable,
    NotDefault,
    SettingDefault,
    Default,
    Failed,
}

public enum QuickPodsOperation
{
    None,
    Connecting,
    SettingDefault,
    Disconnecting,
}

public enum QuickPodsErrorCode
{
    AudioServiceUnavailable,
    BluetoothDriverUnsupported,
    BluetoothTimeout,
    BluetoothSelectionStale,
    BluetoothDeviceUnavailable,
    BluetoothOperationRejected,
    BluetoothContainmentFailed,
    DefaultOutputSwitchFailed,
    NativeHostUnavailable,
    ProtocolMismatch,
}

public readonly record struct BluetoothDeviceKey
{
    [JsonConstructor]
    public BluetoothDeviceKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record BluetoothDeviceState(
    BluetoothDeviceKey Key,
    string DisplayName,
    BluetoothDeviceCapability Capability,
    BluetoothConnectionState ConnectionState,
    DefaultOutputState DefaultOutputState);

public sealed record AudioState(
    AudioCapability Capability,
    int VolumePercent,
    bool IsMuted,
    string? EndpointDisplayName)
{
    public long Generation { get; init; }

    public static AudioState Unavailable { get; } = new(
        AudioCapability.EndpointUnavailable,
        0,
        false,
        null);
}

public sealed record QuickPodsSettings(
    BluetoothDeviceKey? SelectedDevice,
    bool StartWithWindows,
    bool PreferNativeTaskbarSurface)
{
    public static QuickPodsSettings Default { get; } = new(null, false, true);
}

public sealed record QuickPodsState(
    AudioState Audio,
    TaskbarCapability TaskbarCapability,
    DefaultOutputCapability DefaultOutputCapability,
    ImmutableArray<BluetoothDeviceState> BluetoothDevices,
    BluetoothDeviceKey? SelectedDevice,
    QuickPodsOperation Operation,
    QuickPodsErrorCode? Error,
    long Generation)
{
    public static QuickPodsState Initial { get; } = new(
        AudioState.Unavailable,
        TaskbarCapability.HiddenWithTray,
        DefaultOutputCapability.VerificationUnavailable,
        [],
        null,
        QuickPodsOperation.None,
        null,
        0);

    public BluetoothDeviceState? FindSelectedDevice()
    {
        if (SelectedDevice is not { } selected)
        {
            return null;
        }

        foreach (BluetoothDeviceState device in BluetoothDevices)
        {
            if (device.Key == selected)
            {
                return device;
            }
        }

        return null;
    }
}
