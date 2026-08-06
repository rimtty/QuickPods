using System.Runtime.InteropServices;

namespace QuickPods.Windows.Audio.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct KsProperty(Guid set, uint id, uint flags)
{
    internal readonly Guid Set = set;
    internal readonly uint Id = id;
    internal readonly uint Flags = flags;
}

[ComImport]
[Guid("28F54685-06FD-11D2-B27A-00A0C9223196")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IKsControl
{
    [PreserveSig]
    int KsProperty(
        ref KsProperty property,
        uint propertyLength,
        nint propertyData,
        uint dataLength,
        out uint bytesReturned);

    [PreserveSig]
    int KsMethod(
        nint method,
        uint methodLength,
        nint methodData,
        uint dataLength,
        out uint bytesReturned);

    [PreserveSig]
    int KsEvent(
        nint eventData,
        uint eventLength,
        nint eventPayload,
        uint dataLength,
        out uint bytesReturned);
}
