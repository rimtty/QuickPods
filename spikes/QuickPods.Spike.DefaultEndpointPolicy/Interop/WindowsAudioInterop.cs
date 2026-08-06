using System.Runtime.InteropServices;

namespace QuickPods.Spike.DefaultEndpointPolicy.Interop;

internal enum NativeAudioDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum NativeAudioRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey(Guid formatId, uint propertyId)
{
    internal readonly Guid FormatId = formatId;
    internal readonly uint PropertyId = propertyId;
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)]
    internal ushort VariantType;

    [FieldOffset(8)]
    internal uint UnsignedValue;

    [FieldOffset(8)]
    internal nint PointerValue;
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MMDeviceEnumeratorComObject;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(
        NativeAudioDataFlow dataFlow,
        uint stateMask,
        [MarshalAs(UnmanagedType.Interface)] out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(
        NativeAudioDataFlow dataFlow,
        NativeAudioRole role,
        [MarshalAs(UnmanagedType.Interface)] out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IMMNotificationClient client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint deviceCount);

    [PreserveSig]
    int Item(uint index, [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(
        in Guid interfaceId,
        uint classContext,
        nint activationParameters,
        [MarshalAs(UnmanagedType.Interface)] out object activatedInterface);

    [PreserveSig]
    int OpenPropertyStore(
        uint storageMode,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string deviceId);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PropertyKey key);

    [PreserveSig]
    int GetValue(in PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(in PropertyKey key, in PropVariant value);

    [PreserveSig]
    int Commit();
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        uint newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(
        NativeAudioDataFlow dataFlow,
        NativeAudioRole role,
        [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        PropertyKey propertyKey);
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal sealed class PolicyConfigClientComObject;

// Undocumented compatibility boundary. The vtable layout is isolated here and
// must be revalidated for every supported Windows build.
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig]
    int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out nint format);

    [PreserveSig]
    int GetDeviceFormat(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        int defaultFormat,
        out nint format);

    [PreserveSig]
    int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int SetDeviceFormat(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        nint endpointFormat,
        nint mixFormat);

    [PreserveSig]
    int GetProcessingPeriod(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        int defaultPeriod,
        out long defaultValue,
        out long minimumValue);

    [PreserveSig]
    int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, in long period);

    [PreserveSig]
    int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint mode);

    [PreserveSig]
    int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, nint mode);

    [PreserveSig]
    int GetPropertyValue(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        in PropertyKey key,
        out PropVariant value);

    [PreserveSig]
    int SetPropertyValue(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        in PropertyKey key,
        in PropVariant value);

    [PreserveSig]
    int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        NativeAudioRole role);

    [PreserveSig]
    int SetEndpointVisibility(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        int visible);
}

internal static partial class NativeMethods
{
    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint reserved, uint concurrencyModel);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    internal static partial int PropVariantClear(ref PropVariant value);
}

internal static class NativeAudioConstants
{
    internal const uint DeviceStateActive = 0x1;
    internal const uint DeviceStateDisabled = 0x2;
    internal const uint DeviceStateNotPresent = 0x4;
    internal const uint DeviceStateUnplugged = 0x8;
    internal const uint DeviceStateAll = 0xf;
    internal const uint StorageRead = 0;
    internal const uint CoInitMultithreaded = 0;
    internal const ushort VariantTypeUnsignedInt = 19;
    internal const ushort VariantTypeClassId = 72;
    internal const int ElementNotFound = unchecked((int)0x80070490);
}

internal static class ComObject
{
    internal static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            _ = Marshal.ReleaseComObject(instance);
        }
    }
}

internal static class NativeCall
{
    internal static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0)
        {
            throw new WindowsAudioInteropException(operation, result);
        }
    }
}

internal sealed class WindowsAudioInteropException(string operation, int nativeHResult)
    : InvalidOperationException($"{operation} failed with HRESULT 0x{unchecked((uint)nativeHResult):X8}.")
{
    internal string Operation { get; } = operation;

    internal int NativeHResult { get; } = nativeHResult;
}
