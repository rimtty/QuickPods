using System.Runtime.InteropServices;

namespace QuickPods.Spike.BluetoothKs.Interop;

[ComImport]
[Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    [PreserveSig]
    int GetConnectorCount(out uint connectorCount);

    [PreserveSig]
    int GetConnector(
        uint connectorIndex,
        [MarshalAs(UnmanagedType.Interface)] out IConnector connector);

    [PreserveSig]
    int GetSubunitCount(out uint subunitCount);

    [PreserveSig]
    int GetSubunit(uint subunitIndex, out nint subunit);

    [PreserveSig]
    int GetPartById(uint partId, out nint part);

    [PreserveSig]
    int GetDeviceId(out nint deviceId);

    [PreserveSig]
    int GetSignalPath(nint partFrom, nint partTo, int rejectMixedPaths, out nint parts);
}

[ComImport]
[Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    [PreserveSig]
    int GetType(out int connectorType);

    [PreserveSig]
    int GetDataFlow(out int dataFlow);

    [PreserveSig]
    int ConnectTo([MarshalAs(UnmanagedType.Interface)] IConnector connector);

    [PreserveSig]
    int Disconnect();

    [PreserveSig]
    int IsConnected(out int isConnected);

    [PreserveSig]
    int GetConnectedTo([MarshalAs(UnmanagedType.Interface)] out IConnector connector);

    [PreserveSig]
    int GetConnectorIdConnectedTo(out nint connectorId);

    [PreserveSig]
    int GetDeviceIdConnectedTo(out nint deviceId);
}
