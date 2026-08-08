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
    DisconnectingOtherDevices,
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
    OtherBluetoothDevicesStillConnected,
    NativeHostUnavailable,
    ProtocolMismatch,
}

public enum QuickPodsDisplayMode
{
    Auto,
    TrayOnly,
}

public enum QuickPodsTaskbarPlacement
{
    NotificationAreaLeft,
    TaskbarLeft,
}

public enum QuickPodsThemeMode
{
    System,
    Dark,
    Light,
}

public enum QuickPodsLanguageMode
{
    System,
    English,
    Japanese,
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
    bool PreferNativeTaskbarSurface,
    QuickPodsDisplayMode DisplayMode = QuickPodsDisplayMode.Auto,
    bool SetConnectedDeviceAsDefault = true,
    int MouseWheelStepPercent = 2,
    QuickPodsThemeMode Theme = QuickPodsThemeMode.System,
    QuickPodsLanguageMode Language = QuickPodsLanguageMode.System,
    bool ConfirmBluetoothDisconnect = false,
    QuickPodsTaskbarPlacement TaskbarPlacement = QuickPodsTaskbarPlacement.NotificationAreaLeft,
    bool CheckForUpdatesAtStartup = false,
    DateTimeOffset? LastUpdateCheckUtc = null,
    string? LastKnownLatestVersion = null,
    string? LastKnownReleasePage = null,
    int SchemaVersion = 2)
{
    public static QuickPodsSettings Default { get; } = new(null, false, true);

    public QuickPodsSettings Normalize() => this with
    {
        SchemaVersion = 2,
        DisplayMode = Enum.IsDefined(DisplayMode) ? DisplayMode : QuickPodsDisplayMode.Auto,
        TaskbarPlacement = Enum.IsDefined(TaskbarPlacement)
            ? TaskbarPlacement
            : QuickPodsTaskbarPlacement.NotificationAreaLeft,
        MouseWheelStepPercent = MouseWheelStepPercent is 1 or 2 or 5 or 10
            ? MouseWheelStepPercent
            : Default.MouseWheelStepPercent,
        Theme = Enum.IsDefined(Theme) ? Theme : QuickPodsThemeMode.System,
        Language = Enum.IsDefined(Language) ? Language : QuickPodsLanguageMode.System,
    };
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
