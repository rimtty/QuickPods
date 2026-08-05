using System.Runtime.InteropServices;

namespace QuickPods.Spike.BluetoothKs.Interop;

/// <summary>
/// Published Windows KS identifiers used by the Bluetooth-audio spike.
/// These members only construct descriptors; they never send a KS request.
/// </summary>
public static class KernelStreamingContracts
{
    /// <summary>Gets <c>KSPROPSETID_BtAudio</c>.</summary>
    public static Guid BluetoothAudioPropertySet { get; } =
        new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");

    /// <summary>Gets the IID of the user-mode KS proxy <c>IKsControl</c>.</summary>
    public static Guid KsControlInterfaceId { get; } =
        new("28F54685-06FD-11D2-B27A-00A0C9223196");

    /// <summary>Property identifier for <c>KSPROPERTY_ONESHOT_RECONNECT</c>.</summary>
    public const uint OneShotReconnect = 0;

    /// <summary>Property identifier for <c>KSPROPERTY_ONESHOT_DISCONNECT</c>.</summary>
    public const uint OneShotDisconnect = 1;

    /// <summary>KS flag for a get request.</summary>
    public const uint PropertyTypeGet = 0x00000001;

    /// <summary>KS flag for a basic-support query.</summary>
    public const uint PropertyTypeBasicSupport = 0x00000200;

    /// <summary>Size of a native <c>KSPROPERTY</c> descriptor.</summary>
    public static int PropertySize => Marshal.SizeOf<KsProperty>();

    /// <summary>Creates, but does not execute, a basic-support descriptor.</summary>
    public static KsProperty CreateBasicSupportDescriptor(uint propertyId) =>
        new(BluetoothAudioPropertySet, propertyId, PropertyTypeBasicSupport);

    /// <summary>Creates, but does not execute, the documented reconnect GET descriptor.</summary>
    public static KsProperty CreateReconnectDescriptor() =>
        new(BluetoothAudioPropertySet, OneShotReconnect, PropertyTypeGet);

    /// <summary>Creates, but does not execute, the documented disconnect GET descriptor.</summary>
    public static KsProperty CreateDisconnectDescriptor() =>
        new(BluetoothAudioPropertySet, OneShotDisconnect, PropertyTypeGet);
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
