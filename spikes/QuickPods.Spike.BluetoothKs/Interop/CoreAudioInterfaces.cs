using System.Runtime.InteropServices;

namespace QuickPods.Spike.BluetoothKs.Interop;

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(
        NativeDataFlow dataFlow,
        uint stateMask,
        [MarshalAs(UnmanagedType.Interface)] out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(
        NativeDataFlow dataFlow,
        int role,
        [MarshalAs(UnmanagedType.Interface)] out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId,
        [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(nint notificationClient);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(nint notificationClient);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint deviceCount);

    [PreserveSig]
    int Item(
        uint deviceIndex,
        [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);
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
    int GetId(out nint deviceId);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("1BE09788-6894-4089-8586-9A2A6C265AC5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMEndpoint
{
    [PreserveSig]
    int GetDataFlow(out NativeDataFlow dataFlow);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint propertyCount);

    [PreserveSig]
    int GetAt(uint propertyIndex, out PropertyKey key);

    [PreserveSig]
    int GetValue(in PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(in PropertyKey key, in PropVariant value);

    [PreserveSig]
    int Commit();
}
