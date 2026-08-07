using System.Collections.Immutable;
using System.Runtime.InteropServices;
using QuickPods.Windows.Audio.Interop;

namespace QuickPods.Windows.Bluetooth;

internal static partial class WindowsBluetoothPnpInventory
{
    private const uint Success = 0;
    private const uint BufferSmall = 0x1A;
    private const uint GetIdListFilterEnumerator = 0x1;
    private const uint LocateDevNodeNormal = 0;
    private const uint LocateDevNodePhantom = 0x1;
    private const uint DevicePropertyTypeGuid = 0x0D;
    private const uint DevicePropertyTypeString = 0x12;
    private const int MaximumListAttempts = 3;

    private static readonly string[] BluetoothEnumerators =
    [
        "BTHENUM",
        "BTHHFENUM",
        "BTHLEDEVICE",
        "BTHA2DP",
    ];

    private static readonly PropertyKey DeviceContainerId = new(
        new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        2);
    private static readonly PropertyKey DeviceFriendlyName = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);

    internal static IReadOnlyDictionary<Guid, BluetoothPnpContainer> ReadContainers(
        CancellationToken cancellationToken)
    {
        var nodes = ImmutableArray.CreateBuilder<BluetoothPnpNode>();
        foreach (string enumerator in BluetoothEnumerators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string deviceInstanceId in ReadDeviceInstanceIds(enumerator))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryLocateDevice(deviceInstanceId, out uint deviceInstance) ||
                    !TryReadGuid(deviceInstance, DeviceContainerId, out Guid containerId) ||
                    containerId == Guid.Empty)
                {
                    continue;
                }

                nodes.Add(new(
                    containerId,
                    deviceInstanceId,
                    TryReadString(deviceInstance, DeviceFriendlyName)));
            }
        }

        return Collate(nodes);
    }

    internal static IReadOnlyDictionary<Guid, BluetoothPnpContainer> Collate(
        IEnumerable<BluetoothPnpNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return nodes
            .Where(node => node.ContainerId != Guid.Empty)
            .GroupBy(node => node.ContainerId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    BluetoothPnpNode? preferred = group
                        .Where(node => NormalizeName(node.FriendlyName) is not null)
                        .OrderBy(node => ProfileNamePenalty(node.FriendlyName!))
                        .ThenBy(node => node.FriendlyName!.Trim().Length)
                        .ThenBy(node => node.FriendlyName, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    preferred ??= group.First();
                    return new BluetoothPnpContainer(
                        NormalizeName(preferred.FriendlyName) ?? "Bluetooth audio",
                        preferred.DeviceInstanceId);
                });
    }

    private static string? NormalizeName(string? value)
    {
        string? normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static int ProfileNamePenalty(string name) =>
        name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Hands Free", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("AVRCP", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

    private static string[] ReadDeviceInstanceIds(string enumerator)
    {
        for (int attempt = 0; attempt < MaximumListAttempts; attempt++)
        {
            uint result = GetDeviceIdListSize(
                out uint characterCount,
                enumerator,
                GetIdListFilterEnumerator);
            if (result != Success || characterCount <= 1)
            {
                return [];
            }

            nint buffer = Marshal.AllocHGlobal(checked((int)characterCount * sizeof(char)));
            try
            {
                result = GetDeviceIdList(
                    enumerator,
                    buffer,
                    characterCount,
                    GetIdListFilterEnumerator);
                if (result == BufferSmall)
                {
                    continue;
                }

                if (result != Success)
                {
                    return [];
                }

                string list = Marshal.PtrToStringUni(buffer, checked((int)characterCount)) ?? string.Empty;
                return list.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return [];
    }

    private static bool TryLocateDevice(string deviceInstanceId, out uint deviceInstance)
    {
        uint result = LocateDeviceNode(
            out deviceInstance,
            deviceInstanceId,
            LocateDevNodeNormal);
        return result == Success ||
            LocateDeviceNode(
                out deviceInstance,
                deviceInstanceId,
                LocateDevNodePhantom) == Success;
    }

    private static bool TryReadGuid(
        uint deviceInstance,
        PropertyKey key,
        out Guid value)
    {
        value = default;
        if (!TryReadProperty(deviceInstance, key, out uint propertyType, out byte[] bytes) ||
            propertyType != DevicePropertyTypeGuid ||
            bytes.Length != Marshal.SizeOf<Guid>())
        {
            return false;
        }

        value = new Guid(bytes);
        return true;
    }

    private static string? TryReadString(uint deviceInstance, PropertyKey key)
    {
        if (!TryReadProperty(deviceInstance, key, out uint propertyType, out byte[] bytes) ||
            propertyType != DevicePropertyTypeString ||
            bytes.Length < sizeof(char))
        {
            return null;
        }

        string value = System.Text.Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryReadProperty(
        uint deviceInstance,
        PropertyKey key,
        out uint propertyType,
        out byte[] bytes)
    {
        uint byteCount = 0;
        uint result = GetDeviceNodeProperty(
            deviceInstance,
            in key,
            out propertyType,
            nint.Zero,
            ref byteCount,
            0);
        if (result != BufferSmall || byteCount == 0 || byteCount > ushort.MaxValue)
        {
            bytes = [];
            return false;
        }

        nint buffer = Marshal.AllocHGlobal(checked((int)byteCount));
        try
        {
            result = GetDeviceNodeProperty(
                deviceInstance,
                in key,
                out propertyType,
                buffer,
                ref byteCount,
                0);
            if (result != Success)
            {
                bytes = [];
                return false;
            }

            bytes = new byte[byteCount];
            Marshal.Copy(buffer, bytes, 0, checked((int)byteCount));
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDeviceIdListSize(
        out uint characterCount,
        string filter,
        uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDeviceIdList(
        string filter,
        nint buffer,
        uint bufferLength,
        uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint LocateDeviceNode(
        out uint deviceInstance,
        string deviceInstanceId,
        uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    private static partial uint GetDeviceNodeProperty(
        uint deviceInstance,
        in PropertyKey propertyKey,
        out uint propertyType,
        nint propertyBuffer,
        ref uint propertyBufferSize,
        uint flags);
}

internal sealed record BluetoothPnpNode(
    Guid ContainerId,
    string DeviceInstanceId,
    string? FriendlyName);

internal sealed record BluetoothPnpContainer(
    string DisplayName,
    string DeviceInstanceId);
