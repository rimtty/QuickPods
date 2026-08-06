using System.Runtime.InteropServices;
using QuickPods.Spike.DefaultEndpointPolicy.Interop;

namespace QuickPods.Spike.DefaultEndpointPolicy;

internal sealed record DefaultEndpointInventoryRow(
    DefaultEndpointCandidate Candidate,
    string ContainerAlias,
    string EndpointAlias,
    string Label,
    uint FormFactor,
    IReadOnlySet<DefaultEndpointRole> DefaultRoles);

internal sealed record DefaultEndpointInventoryFault(
    string Operation,
    int HResult,
    bool AffectsOwnership);

internal sealed record DefaultEndpointInventorySnapshot(
    string SessionToken,
    IReadOnlyList<DefaultEndpointInventoryRow> Rows,
    IReadOnlyList<DefaultEndpointInventoryFault> Faults);

internal sealed class WindowsDefaultEndpointInventory(EndpointAliasSession aliases)
{
    private static readonly PropertyKey DeviceContainerId = new(
        new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        2);

    private static readonly PropertyKey DeviceFriendlyName = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);

    private static readonly PropertyKey AudioEndpointFormFactor = new(
        new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"),
        0);

    internal DefaultEndpointInventorySnapshot Collect()
    {
        using var worker = new MtaComWorker();
        return worker.Invoke(CollectOnMta);
    }

    private DefaultEndpointInventorySnapshot CollectOnMta()
    {
        var faults = new List<DefaultEndpointInventoryFault>();
        var rows = new List<DefaultEndpointInventoryRow>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? devices = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            NativeCall.ThrowIfFailed(
                enumerator.EnumAudioEndpoints(
                    NativeAudioDataFlow.Render,
                    NativeAudioConstants.DeviceStateAll,
                    out devices),
                nameof(IMMDeviceEnumerator.EnumAudioEndpoints));
            NativeCall.ThrowIfFailed(devices.GetCount(out uint count), nameof(IMMDeviceCollection.GetCount));
            Dictionary<DefaultEndpointRole, string?> defaults = ReadDefaults(enumerator, faults);

            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    NativeCall.ThrowIfFailed(devices.Item(index, out device), nameof(IMMDeviceCollection.Item));
                    DefaultEndpointInventoryRow? row = ReadRow(device, defaults, faults);
                    if (row is not null)
                    {
                        rows.Add(row);
                    }
                }
                catch (WindowsAudioInteropException exception)
                {
                    faults.Add(new DefaultEndpointInventoryFault(
                        exception.Operation,
                        exception.NativeHResult,
                        AffectsOwnership: true));
                }
                finally
                {
                    ComObject.Release(device);
                }
            }
        }
        finally
        {
            ComObject.Release(devices);
            ComObject.Release(enumerator);
        }

        return new DefaultEndpointInventorySnapshot(aliases.Token, rows, faults);
    }

    private DefaultEndpointInventoryRow? ReadRow(
        IMMDevice device,
        IReadOnlyDictionary<DefaultEndpointRole, string?> defaults,
        List<DefaultEndpointInventoryFault> faults)
    {
        NativeCall.ThrowIfFailed(device.GetId(out string endpointId), nameof(IMMDevice.GetId));
        NativeCall.ThrowIfFailed(device.GetState(out uint state), nameof(IMMDevice.GetState));
        IPropertyStore? properties = null;
        try
        {
            NativeCall.ThrowIfFailed(
                device.OpenPropertyStore(NativeAudioConstants.StorageRead, out properties),
                nameof(IMMDevice.OpenPropertyStore));
            Guid? containerId = ReadGuid(properties, DeviceContainerId);
            if (containerId is null || containerId == Guid.Empty)
            {
                faults.Add(new DefaultEndpointInventoryFault(
                    "PKEY_Device_ContainerId",
                    NativeAudioConstants.ElementNotFound,
                    AffectsOwnership: true));
                return null;
            }

            uint? formFactor = ReadUnsigned(properties, AudioEndpointFormFactor);
            if (formFactor is null)
            {
                faults.Add(new DefaultEndpointInventoryFault(
                    "PKEY_AudioEndpoint_FormFactor",
                    NativeAudioConstants.ElementNotFound,
                    AffectsOwnership: false));
            }

            string? friendlyName = null;
            try
            {
                friendlyName = ReadString(properties, DeviceFriendlyName);
            }
            catch (WindowsAudioInteropException exception)
            {
                faults.Add(new DefaultEndpointInventoryFault(
                    "PKEY_Device_FriendlyName",
                    exception.NativeHResult,
                    AffectsOwnership: false));
            }

            string containerValue = containerId.Value.ToString("D");
            var endpoint = new OpaqueEndpointHandle(endpointId);
            var container = new OpaqueContainerHandle(containerValue);
            var candidate = new DefaultEndpointCandidate(
                endpoint,
                container,
                AudioDataFlow.Render,
                ClassifyProfile(formFactor),
                MapAvailability(state));
            HashSet<DefaultEndpointRole> defaultRoles = [.. defaults
                .Where(item => string.Equals(item.Value, endpointId, StringComparison.Ordinal))
                .Select(item => item.Key)];
            return new DefaultEndpointInventoryRow(
                candidate,
                aliases.Alias(containerValue),
                aliases.Alias(endpointId),
                ClassifyLabel(friendlyName),
                formFactor ?? 0,
                defaultRoles);
        }
        finally
        {
            ComObject.Release(properties);
        }
    }

    private static Dictionary<DefaultEndpointRole, string?> ReadDefaults(
        IMMDeviceEnumerator enumerator,
        List<DefaultEndpointInventoryFault> faults)
    {
        var defaults = new Dictionary<DefaultEndpointRole, string?>();
        foreach (DefaultEndpointRole role in Enum.GetValues<DefaultEndpointRole>())
        {
            IMMDevice? device = null;
            try
            {
                int result = enumerator.GetDefaultAudioEndpoint(
                    NativeAudioDataFlow.Render,
                    MapRole(role),
                    out device);
                if (result == NativeAudioConstants.ElementNotFound)
                {
                    defaults[role] = null;
                    continue;
                }

                NativeCall.ThrowIfFailed(result, nameof(IMMDeviceEnumerator.GetDefaultAudioEndpoint));
                NativeCall.ThrowIfFailed(device.GetId(out string endpointId), nameof(IMMDevice.GetId));
                defaults[role] = endpointId;
            }
            catch (WindowsAudioInteropException exception)
            {
                faults.Add(new DefaultEndpointInventoryFault(
                    exception.Operation,
                    exception.NativeHResult,
                    AffectsOwnership: false));
                defaults[role] = null;
            }
            finally
            {
                ComObject.Release(device);
            }
        }

        return defaults;
    }

    private static Guid? ReadGuid(IPropertyStore properties, PropertyKey key)
    {
        PropVariant value = default;
        try
        {
            NativeCall.ThrowIfFailed(properties.GetValue(in key, out value), nameof(IPropertyStore.GetValue));
            return value.VariantType == NativeAudioConstants.VariantTypeClassId &&
                value.PointerValue != nint.Zero
                    ? Marshal.PtrToStructure<Guid>(value.PointerValue)
                    : null;
        }
        finally
        {
            _ = NativeMethods.PropVariantClear(ref value);
        }
    }

    private static uint? ReadUnsigned(IPropertyStore properties, PropertyKey key)
    {
        PropVariant value = default;
        try
        {
            NativeCall.ThrowIfFailed(properties.GetValue(in key, out value), nameof(IPropertyStore.GetValue));
            return value.VariantType == NativeAudioConstants.VariantTypeUnsignedInt
                ? value.UnsignedValue
                : null;
        }
        finally
        {
            _ = NativeMethods.PropVariantClear(ref value);
        }
    }

    private static string? ReadString(IPropertyStore properties, PropertyKey key)
    {
        const ushort VariantTypeWideString = 31;
        PropVariant value = default;
        try
        {
            NativeCall.ThrowIfFailed(properties.GetValue(in key, out value), nameof(IPropertyStore.GetValue));
            return value.VariantType == VariantTypeWideString && value.PointerValue != nint.Zero
                ? Marshal.PtrToStringUni(value.PointerValue)
                : null;
        }
        finally
        {
            _ = NativeMethods.PropVariantClear(ref value);
        }
    }

    private static BluetoothAudioProfile ClassifyProfile(uint? formFactor) => formFactor switch
    {
        1 or 2 or 3 or 8 or 9 or 10 or 11 => BluetoothAudioProfile.Stereo,
        5 or 6 => BluetoothAudioProfile.HandsFree,
        _ => BluetoothAudioProfile.Unknown,
    };

    private static AudioEndpointAvailability MapAvailability(uint state) => state switch
    {
        NativeAudioConstants.DeviceStateActive => AudioEndpointAvailability.Active,
        NativeAudioConstants.DeviceStateDisabled => AudioEndpointAvailability.Disabled,
        NativeAudioConstants.DeviceStateNotPresent => AudioEndpointAvailability.NotPresent,
        NativeAudioConstants.DeviceStateUnplugged => AudioEndpointAvailability.Unplugged,
        _ => AudioEndpointAvailability.NotPresent,
    };

    private static NativeAudioRole MapRole(DefaultEndpointRole role) => role switch
    {
        DefaultEndpointRole.Console => NativeAudioRole.Console,
        DefaultEndpointRole.Multimedia => NativeAudioRole.Multimedia,
        DefaultEndpointRole.Communications => NativeAudioRole.Communications,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static string ClassifyLabel(string? friendlyName)
    {
        if (friendlyName?.Contains("AirPods", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "AirPods audio";
        }

        if (friendlyName?.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Bluetooth audio";
        }

        return "Audio endpoint";
    }
}
