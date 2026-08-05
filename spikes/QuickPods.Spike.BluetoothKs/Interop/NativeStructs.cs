using System.Runtime.InteropServices;

namespace QuickPods.Spike.BluetoothKs.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct PropertyKey
{
    internal PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }

    internal readonly Guid FormatId;
    internal readonly uint PropertyId;
}

// QuickPods is x64-only. Native PROPVARIANT is 24 bytes on x64; its value
// union begins at offset 8. Only VT_LPWSTR and VT_CLSID are read here.
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)]
    internal ushort VariantType;

    [FieldOffset(8)]
    internal nint PointerValue;
}

/// <summary>
/// Managed layout of the Windows KS <c>KSPROPERTY</c> structure.
/// </summary>
/// <remarks>
/// Initializes a KS property descriptor. This structure does not execute a request.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct KsProperty(Guid set, uint id, uint flags)
{
    internal readonly Guid SetValue = set;
    internal readonly uint IdValue = id;
    internal readonly uint FlagsValue = flags;

    /// <summary>Gets the KS property-set identifier.</summary>
    public Guid Set => SetValue;

    /// <summary>Gets the property identifier within the set.</summary>
    public uint Id => IdValue;

    /// <summary>Gets the KS request flags.</summary>
    public uint Flags => FlagsValue;
}
