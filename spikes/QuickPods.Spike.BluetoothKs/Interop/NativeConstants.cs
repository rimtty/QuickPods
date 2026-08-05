namespace QuickPods.Spike.BluetoothKs.Interop;

internal static class NativeConstants
{
    internal const uint DeviceStateActive = 0x00000001;
    internal const uint DeviceStateDisabled = 0x00000002;
    internal const uint DeviceStateNotPresent = 0x00000004;
    internal const uint DeviceStateUnplugged = 0x00000008;
    internal const uint DeviceStateMaskAll = 0x0000000f;

    internal const uint StorageModeRead = 0;
    internal const uint ClsContextAll = 0x00000017;

    internal const uint CoInitMultithreaded = 0;

    internal const ushort VariantTypeWideString = 31;
    internal const ushort VariantTypeClassId = 72;

    internal const int Success = 0;
    internal const int SuccessAlreadyInitialized = 1;
}

internal enum NativeDataFlow
{
    Unknown = -1,
    Render = 0,
    Capture = 1,
    All = 2,
}
