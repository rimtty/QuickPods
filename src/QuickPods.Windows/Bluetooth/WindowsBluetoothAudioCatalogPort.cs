using System.Collections.Immutable;
using System.Runtime.InteropServices;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Windows.Audio.Interop;
using Windows.Devices.Enumeration;

namespace QuickPods.Windows.Bluetooth;

public sealed partial class WindowsBluetoothAudioCatalogPort : IBluetoothAudioCatalogPort, IDisposable
{
    private const string PairedBluetoothAepSelector =
        "(System.Devices.Aep.ProtocolId:=\"{E0CBF06C-CD8B-4647-BB8A-263B43F0F974}\" OR " +
        "System.Devices.Aep.ProtocolId:=\"{BB7BB05E-5972-42B5-94FC-76EAA7084D49}\") AND " +
        "System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";
    private const ushort VariantTypeUnsignedInt = 19;
    private const ushort VariantTypeWideString = 31;
    private const ushort VariantTypeClassId = 72;
    private const uint StorageModeRead = 0;
    private const int ElementNotFound = unchecked((int)0x80070490);
    private const int PathNotFound = unchecked((int)0x80070003);

    private static readonly PropertyKey DeviceFriendlyName = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);
    private static readonly PropertyKey DeviceContainerId = new(
        new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        2);
    private static readonly PropertyKey AudioEndpointFormFactor = new(
        new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"),
        0);
    private static readonly string[] AepProperties =
    [
        "System.Devices.Aep.ContainerId",
        "System.Devices.Aep.IsPaired",
        "System.Devices.Aep.ProtocolId",
        "System.Devices.Aep.Category",
        "System.ItemNameDisplay",
    ];
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(20);

    private readonly MtaAudioWorker worker = new();
    private readonly WindowsBluetoothBindingRegistry bindingRegistry = new();
    private readonly WindowsBluetoothCapabilityProbe capabilityProbe;
    private int disposed;

    public WindowsBluetoothAudioCatalogPort()
    {
        capabilityProbe = new WindowsBluetoothCapabilityProbe(
            Path.Combine(AppContext.BaseDirectory, "QuickPods.BluetoothWorker.exe"));
    }

    public async ValueTask<BluetoothAudioCatalogObservation> DiscoverAsync(
        long inventoryGeneration,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        ArgumentOutOfRangeException.ThrowIfNegative(inventoryGeneration);
        if (WindowsSessionContext.IsRemoteSession)
        {
            // Association-endpoint discovery can remain pending for the lifetime of an RDP
            // session. A remote audio redirector is not evidence about the console user's
            // paired Bluetooth inventory, so return a safe read-only empty observation.
            _ = bindingRegistry.Publish(inventoryGeneration, []);
            return new BluetoothAudioCatalogObservation(inventoryGeneration, []);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DiscoveryTimeout);
        IReadOnlyDictionary<Guid, AepContainerMetadata> containers =
            await ReadPairedBluetoothContainersAsync(timeout.Token).ConfigureAwait(false);
        WindowsBluetoothDiscovery discovery = await worker.InvokeAsync(
            () => ReadAudioEndpoints(containers),
            timeout.Token).ConfigureAwait(false);
        IReadOnlyDictionary<BluetoothDeviceKey, BluetoothDeviceCapability> capabilities =
            await ProbeCapabilitiesAsync(discovery.Bindings, timeout.Token).ConfigureAwait(false);
        _ = bindingRegistry.Publish(inventoryGeneration, discovery.Bindings);
        return new BluetoothAudioCatalogObservation(
            inventoryGeneration,
            discovery.Endpoints.Select(endpoint => endpoint with
            {
                Capability = capabilities.GetValueOrDefault(
                    endpoint.DeviceKey,
                    BluetoothDeviceCapability.OwnershipUnknown),
            }));
    }

    internal bool TryResolveBinding(
        BluetoothOperationTarget target,
        out WindowsBluetoothDeviceBinding binding) =>
        bindingRegistry.TryResolve(target, out binding);

    private async Task<IReadOnlyDictionary<BluetoothDeviceKey, BluetoothDeviceCapability>>
        ProbeCapabilitiesAsync(
            ImmutableArray<WindowsBluetoothDeviceBinding> bindings,
            CancellationToken cancellationToken)
    {
        var capabilities = new Dictionary<BluetoothDeviceKey, BluetoothDeviceCapability>();
        foreach (WindowsBluetoothDeviceBinding binding in bindings)
        {
            BluetoothDeviceCapability capability;
            try
            {
                capability = await capabilityProbe.ProbeAsync(binding, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                capability = BluetoothDeviceCapability.TemporarilyUnavailable;
            }

            capabilities.Add(binding.DeviceKey, capability);
        }

        return capabilities;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            worker.Dispose();
        }
    }

    private static async Task<IReadOnlyDictionary<Guid, AepContainerMetadata>>
        ReadPairedBluetoothContainersAsync(CancellationToken cancellationToken)
    {
        DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(
                PairedBluetoothAepSelector,
                AepProperties,
                DeviceInformationKind.AssociationEndpoint)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        var result = new Dictionary<Guid, AepContainerMetadata>();
        foreach (DeviceInformation device in devices)
        {
            if (!ReadBoolean(device, "System.Devices.Aep.IsPaired") ||
                !TryReadGuid(device, "System.Devices.Aep.ContainerId", out Guid containerId) ||
                containerId == Guid.Empty)
            {
                continue;
            }

            var candidate = new AepContainerMetadata(
                string.IsNullOrWhiteSpace(device.Name) ? "Bluetooth audio" : device.Name.Trim(),
                ReadStringArray(device, "System.Devices.Aep.Category"));
            result[containerId] = result.TryGetValue(containerId, out AepContainerMetadata? existing)
                ? existing.Merge(candidate)
                : candidate;
        }

        return result;
    }

    private static WindowsBluetoothDiscovery ReadAudioEndpoints(
        IReadOnlyDictionary<Guid, AepContainerMetadata> containers)
    {
        if (containers.Count == 0)
        {
            return new([], []);
        }

        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        var endpoints = ImmutableArray.CreateBuilder<WindowsBluetoothEndpointDiscovery>();
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            HResult.ThrowIfFailed(
                enumerator.EnumAudioEndpoints(
                    AudioDataFlow.All,
                    AudioDeviceState.Active |
                    AudioDeviceState.Disabled |
                    AudioDeviceState.NotPresent |
                    AudioDeviceState.Unplugged,
                    out collection),
                nameof(IMMDeviceEnumerator.EnumAudioEndpoints));
            string? consoleDefaultId = TryReadDefaultRenderEndpointId(
                enumerator,
                AudioRole.Console);
            string? multimediaDefaultId = TryReadDefaultRenderEndpointId(
                enumerator,
                AudioRole.Multimedia);
            HResult.ThrowIfFailed(collection.GetCount(out uint count), nameof(IMMDeviceCollection.GetCount));
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    HResult.ThrowIfFailed(collection.Item(index, out device), nameof(IMMDeviceCollection.Item));
                    if (TryReadEndpoint(
                        device,
                        containers,
                        consoleDefaultId,
                        multimediaDefaultId,
                        out WindowsBluetoothEndpointDiscovery endpoint))
                    {
                        endpoints.Add(endpoint);
                    }
                }
                catch (Exception exception) when (IsEndpointLocalFailure(exception))
                {
                    // A broken endpoint must not prevent unrelated physical devices from being cataloged.
                }
                finally
                {
                    ReleaseComObject(device);
                }
            }
        }
        finally
        {
            ReleaseComObject(collection);
            ReleaseComObject(enumerator);
        }

        ImmutableArray<WindowsBluetoothEndpointDiscovery> discovered = endpoints.ToImmutable();
        ImmutableArray<WindowsBluetoothDeviceBinding> bindings = MarkAmbiguousAdapterOwnership([.. discovered
            .GroupBy(endpoint => endpoint.Evidence.DeviceKey)
            .Select(group => new WindowsBluetoothDeviceBinding(
                group.Key,
                group.First().ContainerId,
                [.. group.Select(endpoint => endpoint.Binding)],
                HasAmbiguousAdapterOwnership: false))]);
        return new(
            [.. discovered.Select(endpoint => endpoint.Evidence)],
            bindings);
    }

    internal static ImmutableArray<WindowsBluetoothDeviceBinding> MarkAmbiguousAdapterOwnership(
        ImmutableArray<WindowsBluetoothDeviceBinding> bindings)
    {
        HashSet<string> sharedAdapterIds = [.. bindings
            .SelectMany(binding => binding.Endpoints.SelectMany(endpoint =>
                endpoint.AdapterDeviceIds.Select(adapterId => (binding.DeviceKey, AdapterId: adapterId))))
            .GroupBy(candidate => candidate.AdapterId, StringComparer.Ordinal)
            .Where(group => group.Select(candidate => candidate.DeviceKey).Distinct().Skip(1).Any())
            .Select(group => group.Key)];
        if (sharedAdapterIds.Count == 0)
        {
            return bindings;
        }

        return [.. bindings.Select(binding => binding with
        {
            HasAmbiguousAdapterOwnership = binding.Endpoints.Any(endpoint =>
                endpoint.AdapterDeviceIds.Any(sharedAdapterIds.Contains)),
        })];
    }

    private static bool TryReadEndpoint(
        IMMDevice device,
        IReadOnlyDictionary<Guid, AepContainerMetadata> containers,
        string? consoleDefaultId,
        string? multimediaDefaultId,
        out WindowsBluetoothEndpointDiscovery endpoint)
    {
        endpoint = null!;
        HResult.ThrowIfFailed(device.GetState(out AudioDeviceState state), nameof(IMMDevice.GetState));
        var nativeEndpoint = (IMMEndpoint)device;
        HResult.ThrowIfFailed(nativeEndpoint.GetDataFlow(out AudioDataFlow flow), nameof(IMMEndpoint.GetDataFlow));
        if (flow is not AudioDataFlow.Render and not AudioDataFlow.Capture)
        {
            return false;
        }

        HResult.ThrowIfFailed(device.GetId(out string endpointId), nameof(IMMDevice.GetId));
        IPropertyStore? properties = null;
        try
        {
            HResult.ThrowIfFailed(
                device.OpenPropertyStore(StorageModeRead, out properties),
                nameof(IMMDevice.OpenPropertyStore));
            Guid? containerId = ReadGuidProperty(properties, DeviceContainerId);
            if (containerId is not Guid id || !containers.TryGetValue(id, out AepContainerMetadata? metadata))
            {
                return false;
            }

            uint formFactor = ReadUnsignedIntProperty(properties, AudioEndpointFormFactor) ?? uint.MaxValue;
            string displayName = metadata.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = ReadStringProperty(properties, DeviceFriendlyName) ?? "Bluetooth audio";
            }

            BluetoothDeviceKey deviceKey = BluetoothDeviceKeyFactory.Create(id);
            BluetoothAudioProfile profile = ResolveProfile(flow, formFactor);
            BluetoothEndpointDirection direction = flow == AudioDataFlow.Render
                ? BluetoothEndpointDirection.Render
                : BluetoothEndpointDirection.Capture;
            BluetoothEndpointAvailability availability = MapAvailability(state);
            ImmutableArray<string> adapterDeviceIds = ReadAdapterDeviceIds(device);
            endpoint = new(
                id,
                new BluetoothAudioEndpointEvidence(
                    deviceKey,
                    displayName,
                    ResolveKind(metadata.Categories, formFactor),
                    profile,
                    direction,
                    availability,
                    BluetoothDeviceCapability.OwnershipUnknown,
                    IsPaired: true,
                    IsBluetooth: true)
                {
                    IsConsoleDefault = string.Equals(
                        endpointId,
                        consoleDefaultId,
                        StringComparison.Ordinal),
                    IsMultimediaDefault = string.Equals(
                        endpointId,
                        multimediaDefaultId,
                        StringComparison.Ordinal),
                },
                new(
                    endpointId,
                    direction,
                    profile,
                    availability,
                    adapterDeviceIds));
            return true;
        }
        finally
        {
            ReleaseComObject(properties);
        }
    }

    private static string? TryReadDefaultRenderEndpointId(
        IMMDeviceEnumerator enumerator,
        AudioRole role)
    {
        IMMDevice? device = null;
        try
        {
            int result = enumerator.GetDefaultAudioEndpoint(AudioDataFlow.Render, role, out device);
            if (result == ElementNotFound)
            {
                return null;
            }

            HResult.ThrowIfFailed(result, nameof(IMMDeviceEnumerator.GetDefaultAudioEndpoint));
            HResult.ThrowIfFailed(device.GetId(out string endpointId), nameof(IMMDevice.GetId));
            return endpointId;
        }
        catch (Exception exception) when (IsEndpointLocalFailure(exception))
        {
            return null;
        }
        finally
        {
            ReleaseComObject(device);
        }
    }

    private static BluetoothAudioKind ResolveKind(
        ImmutableArray<string> categories,
        uint formFactor)
    {
        if (categories.Any(category => category.Contains("Speaker", StringComparison.OrdinalIgnoreCase)))
        {
            return BluetoothAudioKind.Speaker;
        }

        if (categories.Any(category => category.Contains("Headset", StringComparison.OrdinalIgnoreCase)))
        {
            return BluetoothAudioKind.Headset;
        }

        if (categories.Any(category => category.Contains("Headphone", StringComparison.OrdinalIgnoreCase)))
        {
            return BluetoothAudioKind.Headphones;
        }

        return formFactor switch
        {
            1 => BluetoothAudioKind.Speaker,
            3 => BluetoothAudioKind.Headphones,
            5 or 6 => BluetoothAudioKind.Headset,
            _ => BluetoothAudioKind.Unknown,
        };
    }

    private static BluetoothAudioProfile ResolveProfile(AudioDataFlow flow, uint formFactor) =>
        flow == AudioDataFlow.Capture || formFactor is 5 or 6
            ? BluetoothAudioProfile.HandsFree
            : formFactor is 1 or 3
                ? BluetoothAudioProfile.Stereo
                : BluetoothAudioProfile.Other;

    private static BluetoothEndpointAvailability MapAvailability(AudioDeviceState state)
    {
        if ((state & AudioDeviceState.Active) != 0)
        {
            return BluetoothEndpointAvailability.Active;
        }

        if ((state & AudioDeviceState.Disabled) != 0)
        {
            return BluetoothEndpointAvailability.Disabled;
        }

        if ((state & AudioDeviceState.NotPresent) != 0)
        {
            return BluetoothEndpointAvailability.NotPresent;
        }

        if ((state & AudioDeviceState.Unplugged) != 0)
        {
            return BluetoothEndpointAvailability.Unplugged;
        }

        return BluetoothEndpointAvailability.Unknown;
    }

    private static ImmutableArray<string> ReadAdapterDeviceIds(IMMDevice device)
    {
        object? activatedInterface = null;
        try
        {
            Guid topologyInterfaceId = typeof(IDeviceTopology).GUID;
            int activationResult = device.Activate(
                ref topologyInterfaceId,
                ComClassContext.All,
                nint.Zero,
                out activatedInterface);
            if (activationResult < 0)
            {
                return [];
            }

            var topology = (IDeviceTopology)activatedInterface;
            HResult.ThrowIfFailed(
                topology.GetConnectorCount(out uint connectorCount),
                nameof(IDeviceTopology.GetConnectorCount));
            var adapterIds = new HashSet<string>(StringComparer.Ordinal);
            for (uint index = 0; index < connectorCount; index++)
            {
                IConnector? connector = null;
                try
                {
                    HResult.ThrowIfFailed(
                        topology.GetConnector(index, out connector),
                        nameof(IDeviceTopology.GetConnector));
                    nint adapterIdPointer = nint.Zero;
                    try
                    {
                        int result = connector.GetDeviceIdConnectedTo(out adapterIdPointer);
                        if (result is PathNotFound or ElementNotFound)
                        {
                            continue;
                        }

                        HResult.ThrowIfFailed(result, nameof(IConnector.GetDeviceIdConnectedTo));
                        string? adapterId = Marshal.PtrToStringUni(adapterIdPointer);
                        if (!string.IsNullOrWhiteSpace(adapterId))
                        {
                            adapterIds.Add(adapterId);
                        }
                    }
                    finally
                    {
                        if (adapterIdPointer != nint.Zero)
                        {
                            Marshal.FreeCoTaskMem(adapterIdPointer);
                        }
                    }
                }
                finally
                {
                    ReleaseComObject(connector);
                }
            }

            return [.. adapterIds.Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (IsEndpointLocalFailure(exception))
        {
            return [];
        }
        finally
        {
            ReleaseComObject(activatedInterface);
        }
    }

    private static Guid? ReadGuidProperty(IPropertyStore properties, PropertyKey key)
    {
        PropVariant value = default;
        try
        {
            HResult.ThrowIfFailed(properties.GetValue(ref key, out value), nameof(IPropertyStore.GetValue));
            return value.VariantType == VariantTypeClassId && value.PointerValue != nint.Zero
                ? Marshal.PtrToStructure<Guid>(value.PointerValue)
                : null;
        }
        finally
        {
            _ = CoreAudioNativeMethods.PropVariantClear(ref value);
        }
    }

    private static string? ReadStringProperty(IPropertyStore properties, PropertyKey key)
    {
        PropVariant value = default;
        try
        {
            HResult.ThrowIfFailed(properties.GetValue(ref key, out value), nameof(IPropertyStore.GetValue));
            return value.VariantType == VariantTypeWideString && value.PointerValue != nint.Zero
                ? Marshal.PtrToStringUni(value.PointerValue)
                : null;
        }
        finally
        {
            _ = CoreAudioNativeMethods.PropVariantClear(ref value);
        }
    }

    private static uint? ReadUnsignedIntProperty(IPropertyStore properties, PropertyKey key)
    {
        PropVariant value = default;
        try
        {
            HResult.ThrowIfFailed(properties.GetValue(ref key, out value), nameof(IPropertyStore.GetValue));
            return value.VariantType == VariantTypeUnsignedInt
                ? unchecked((uint)value.PointerValue.ToInt64())
                : null;
        }
        finally
        {
            _ = CoreAudioNativeMethods.PropVariantClear(ref value);
        }
    }

    private static bool ReadBoolean(DeviceInformation device, string key) =>
        device.Properties.TryGetValue(key, out object? value) && value is true;

    private static bool TryReadGuid(DeviceInformation device, string key, out Guid value)
    {
        if (device.Properties.TryGetValue(key, out object? property) && property is Guid guid)
        {
            value = guid;
            return true;
        }

        value = default;
        return false;
    }

    private static ImmutableArray<string> ReadStringArray(DeviceInformation device, string key) =>
        device.Properties.TryGetValue(key, out object? value) && value is IEnumerable<string> values
            ? [.. values.Where(item => !string.IsNullOrWhiteSpace(item))]
            : [];

    private static bool IsEndpointLocalFailure(Exception exception) =>
        exception is COMException or InvalidCastException or CoreAudioInteropException;

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.ReleaseComObject(value);
        }
    }

    private sealed record AepContainerMetadata(
        string DisplayName,
        ImmutableArray<string> Categories)
    {
        internal AepContainerMetadata Merge(AepContainerMetadata other) =>
            new(
                new[] { DisplayName, other.DisplayName }
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .First(),
                [.. Categories
                    .Concat(other.Categories)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)]);
    }

    private sealed record WindowsBluetoothEndpointDiscovery(
        Guid ContainerId,
        BluetoothAudioEndpointEvidence Evidence,
        WindowsBluetoothEndpointBinding Binding);

    private sealed record WindowsBluetoothDiscovery(
        ImmutableArray<BluetoothAudioEndpointEvidence> Endpoints,
        ImmutableArray<WindowsBluetoothDeviceBinding> Bindings);

}
