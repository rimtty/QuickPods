namespace QuickPods.Core.Models;

public enum AudioCapability
{
    Available,
    ServiceUnavailable,
    EndpointUnavailable,
}

public enum TaskbarCapability
{
    NativeAvailable,
    FloatingOnly,
    HiddenWithTray,
}

public enum DefaultOutputCapability
{
    Supported,
    PolicyUnavailable,
    VerificationUnavailable,
}

public enum BluetoothDeviceCapability
{
    DirectControl,
    SettingsOnly,
    OwnershipUnknown,
    TemporarilyUnavailable,
}
