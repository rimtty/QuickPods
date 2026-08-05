using System.Runtime.InteropServices;
using QuickPods.Spike.BluetoothKs.Diagnostics;
using QuickPods.Spike.BluetoothKs.Interop;

namespace QuickPods.Spike.BluetoothKs.Discovery;

internal sealed class BluetoothKsDiscoveryService(IdentifierHasher hasher) : IBluetoothKsDiscovery
{
    private const int ElementNotFound = unchecked((int)0x80070490);
    private const int PathNotFound = unchecked((int)0x80070003);

    private static readonly PropertyKey DeviceFriendlyName = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);

    private static readonly PropertyKey DeviceContainerId = new(
        new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        2);

    public BluetoothKsDiscoveryResult Discover()
    {
        using var apartment = ComApartmentScope.EnterMultithreaded();
        var faults = new List<DiscoveryFault>();
        var rawEndpoints = new List<RawEndpoint>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            ThrowIfFailed(
                enumerator.EnumAudioEndpoints(
                    NativeDataFlow.All,
                    NativeConstants.DeviceStateMaskAll,
                    out collection),
                nameof(IMMDeviceEnumerator.EnumAudioEndpoints));
            ThrowIfFailed(collection.GetCount(out uint count), nameof(IMMDeviceCollection.GetCount));

            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    ThrowIfFailed(collection.Item(index, out device), nameof(IMMDeviceCollection.Item));
                    RawEndpoint? endpoint = ReadEndpoint(device, faults);
                    if (endpoint is not null)
                    {
                        rawEndpoints.Add(endpoint);
                    }
                }
                catch (NativeCallException exception)
                {
                    faults.Add(new DiscoveryFault(exception.Operation, exception.NativeHResult));
                }
                catch (InvalidCastException)
                {
                    faults.Add(new DiscoveryFault("IMMEndpoint.QueryInterface", unchecked((int)0x80004002)));
                }
                finally
                {
                    ComObject.Release(device);
                }
            }
        }
        finally
        {
            ComObject.Release(collection);
            ComObject.Release(enumerator);
        }

        return BuildResult(rawEndpoints, faults);
    }

    private RawEndpoint? ReadEndpoint(IMMDevice device, List<DiscoveryFault> faults)
    {
        nint endpointIdPointer = nint.Zero;
        string endpointId;
        try
        {
            ThrowIfFailed(device.GetId(out endpointIdPointer), nameof(IMMDevice.GetId));
            endpointId = Marshal.PtrToStringUni(endpointIdPointer) ??
                throw new InvalidOperationException("IMMDevice.GetId returned an empty identifier.");
        }
        finally
        {
            CoTaskMemory.Free(ref endpointIdPointer);
        }
        ThrowIfFailed(device.GetState(out uint state), nameof(IMMDevice.GetState));
        NativeDataFlow flow = ReadDataFlow(device);
        Guid? containerId = null;
        string? friendlyName = null;
        IPropertyStore? properties = null;
        try
        {
            int openResult = device.OpenPropertyStore(
                NativeConstants.StorageModeRead,
                out properties);
            if (openResult < 0)
            {
                faults.Add(new DiscoveryFault(
                    nameof(IMMDevice.OpenPropertyStore),
                    openResult,
                    AffectsOwnership: false));
            }
            else
            {
                bool containerReadFailed = false;
                try
                {
                    containerId = ReadGuid(properties, DeviceContainerId);
                }
                catch (NativeCallException exception)
                {
                    containerReadFailed = true;
                    faults.Add(new DiscoveryFault(
                        "PKEY_Device_ContainerId",
                        exception.NativeHResult,
                        AffectsOwnership: false));
                }

                if (!containerReadFailed &&
                    (containerId is null || containerId == Guid.Empty))
                {
                    faults.Add(new DiscoveryFault(
                        "PKEY_Device_ContainerId",
                        ElementNotFound,
                        AffectsOwnership: false));
                }

                try
                {
                    friendlyName = ReadString(properties, DeviceFriendlyName);
                }
                catch (NativeCallException exception)
                {
                    faults.Add(new DiscoveryFault(
                        "PKEY_Device_FriendlyName",
                        exception.NativeHResult,
                        AffectsOwnership: false));
                }
            }
        }
        finally
        {
            ComObject.Release(properties);
        }

        string[] adapterIds = ReadAdapterDeviceIds(device, faults);
        return new RawEndpoint(
            containerId == Guid.Empty ? null : containerId,
            endpointId,
            friendlyName,
            flow,
            state,
            adapterIds);
    }

    private static NativeDataFlow ReadDataFlow(IMMDevice device)
    {
        var endpoint = (IMMEndpoint)device;
        ThrowIfFailed(endpoint.GetDataFlow(out NativeDataFlow flow), nameof(IMMEndpoint.GetDataFlow));
        return flow;
    }

    private static Guid? ReadGuid(IPropertyStore properties, PropertyKey propertyKey)
    {
        PropVariant value = default;
        try
        {
            ThrowIfFailed(
                properties.GetValue(in propertyKey, out value),
                nameof(IPropertyStore.GetValue));
            return value.VariantType == NativeConstants.VariantTypeClassId &&
                value.PointerValue != nint.Zero
                    ? Marshal.PtrToStructure<Guid>(value.PointerValue)
                    : null;
        }
        finally
        {
            _ = NativeMethods.PropVariantClear(ref value);
        }
    }

    private static string? ReadString(IPropertyStore properties, PropertyKey propertyKey)
    {
        PropVariant value = default;
        try
        {
            ThrowIfFailed(
                properties.GetValue(in propertyKey, out value),
                nameof(IPropertyStore.GetValue));
            return value.VariantType == NativeConstants.VariantTypeWideString &&
                value.PointerValue != nint.Zero
                    ? Marshal.PtrToStringUni(value.PointerValue)
                    : null;
        }
        finally
        {
            _ = NativeMethods.PropVariantClear(ref value);
        }
    }

    private static string[] ReadAdapterDeviceIds(
        IMMDevice device,
        List<DiscoveryFault> faults)
    {
        var adapterIds = new HashSet<string>(StringComparer.Ordinal);
        IDeviceTopology? topology = null;
        object? activatedInterface = null;
        try
        {
            Guid topologyInterfaceId = typeof(IDeviceTopology).GUID;
            int activationResult = device.Activate(
                in topologyInterfaceId,
                NativeConstants.ClsContextAll,
                nint.Zero,
                out activatedInterface);
            if (activationResult < 0)
            {
                faults.Add(new DiscoveryFault(nameof(IDeviceTopology), activationResult));
                return [];
            }

            topology = (IDeviceTopology)activatedInterface;
            ThrowIfFailed(
                topology.GetConnectorCount(out uint connectorCount),
                nameof(IDeviceTopology.GetConnectorCount));
            if (connectorCount == 0)
            {
                return [];
            }

            IConnector? connector = null;
            try
            {
                ThrowIfFailed(
                    topology.GetConnector(0, out connector),
                    nameof(IDeviceTopology.GetConnector));
                nint adapterIdPointer = nint.Zero;
                try
                {
                    int result = connector.GetDeviceIdConnectedTo(out adapterIdPointer);
                    if (result != PathNotFound && result != ElementNotFound)
                    {
                        ThrowIfFailed(result, nameof(IConnector.GetDeviceIdConnectedTo));
                        string? adapterDeviceId = Marshal.PtrToStringUni(adapterIdPointer);
                        if (!string.IsNullOrWhiteSpace(adapterDeviceId))
                        {
                            adapterIds.Add(adapterDeviceId);
                        }
                    }
                }
                finally
                {
                    CoTaskMemory.Free(ref adapterIdPointer);
                }
            }
            catch (NativeCallException exception)
            {
                faults.Add(new DiscoveryFault(exception.Operation, exception.NativeHResult));
            }
            finally
            {
                ComObject.Release(connector);
            }
        }
        finally
        {
            ComObject.Release(activatedInterface);
        }

        return [.. adapterIds];
    }

    private BluetoothKsDiscoveryResult BuildResult(
        List<RawEndpoint> endpoints,
        List<DiscoveryFault> faults)
    {
        var groups = new List<BluetoothDeviceGroup>();
        var targets = new Dictionary<string, RawKsTarget>(StringComparer.Ordinal);
        string[] unassignedAdapterDeviceIds = [.. endpoints
            .Where(endpoint => endpoint.ContainerId is null)
            .SelectMany(endpoint => endpoint.AdapterDeviceIds)
            .Distinct(StringComparer.Ordinal)];

        foreach (IGrouping<Guid, RawEndpoint> container in endpoints
            .Where(endpoint => endpoint.ContainerId is not null)
            .GroupBy(endpoint => endpoint.ContainerId!.Value))
        {
            RawEndpoint[] containerEndpoints = [.. container];
            string containerHash = hasher.Hash(container.Key.ToString("D"));
            SanitizedAudioEndpoint[] sanitizedEndpoints = [.. containerEndpoints
                .Select(endpoint => new SanitizedAudioEndpoint(
                    hasher.Hash(endpoint.EndpointId),
                    DeviceLabelSanitizer.Classify(endpoint.FriendlyName),
                    endpoint.Flow,
                    endpoint.State))
                .OrderBy(endpoint => endpoint.EndpointHash, StringComparer.Ordinal)];
            SanitizedKsFilterCandidate[] candidates = [.. containerEndpoints
                .SelectMany(endpoint => endpoint.AdapterDeviceIds.Select(adapterId => new
                {
                    AdapterId = adapterId,
                    SourceEndpointHash = hasher.Hash(endpoint.EndpointId),
                    SourceFlow = endpoint.Flow,
                }))
                .GroupBy(
                    candidate => (candidate.AdapterId, candidate.SourceFlow),
                    new AdapterFlowComparer())
                .Select(candidate => new SanitizedKsFilterCandidate(
                    hasher.Hash(candidate.Key.AdapterId),
                    candidate.First().SourceEndpointHash,
                    candidate.Key.SourceFlow,
                    "not-probed"))
                .OrderBy(candidate => candidate.FilterHash, StringComparer.Ordinal)];
            string label = sanitizedEndpoints.Any(endpoint => endpoint.Label == "AirPods audio")
                ? "AirPods audio"
                : sanitizedEndpoints.Any(endpoint => endpoint.Label == "Bluetooth audio")
                    ? "Bluetooth audio"
                    : "Audio device";

            groups.Add(new BluetoothDeviceGroup(
                containerHash,
                label,
                sanitizedEndpoints,
                candidates));
            targets.Add(containerHash, new RawKsTarget(
                containerHash,
                [.. containerEndpoints
                    .SelectMany(endpoint => endpoint.AdapterDeviceIds.Select(adapterId =>
                        new RawKsCandidate(adapterId, endpoint.Flow)))
                    .Distinct(new RawKsCandidateComparer())]));
        }

        return new BluetoothKsDiscoveryResult(
            new BluetoothKsInventory(
                hasher.SessionToken,
                [.. groups.OrderBy(group => group.ContainerHash, StringComparer.Ordinal)],
                [.. faults]),
            targets,
            unassignedAdapterDeviceIds);
    }

    private static void ThrowIfFailed(int result, string operation)
    {
        if (result < 0)
        {
            throw new NativeCallException(operation, result);
        }
    }

    private sealed record RawEndpoint(
        Guid? ContainerId,
        string EndpointId,
        string? FriendlyName,
        NativeDataFlow Flow,
        uint State,
        IReadOnlyList<string> AdapterDeviceIds);

    private sealed class AdapterFlowComparer : IEqualityComparer<(string AdapterId, NativeDataFlow SourceFlow)>
    {
        public bool Equals(
            (string AdapterId, NativeDataFlow SourceFlow) left,
            (string AdapterId, NativeDataFlow SourceFlow) right) =>
            left.SourceFlow == right.SourceFlow &&
            StringComparer.Ordinal.Equals(left.AdapterId, right.AdapterId);

        public int GetHashCode((string AdapterId, NativeDataFlow SourceFlow) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.AdapterId),
                value.SourceFlow);
    }

    private sealed class RawKsCandidateComparer : IEqualityComparer<RawKsCandidate>
    {
        public bool Equals(RawKsCandidate? left, RawKsCandidate? right) =>
            ReferenceEquals(left, right) ||
            left is not null &&
            right is not null &&
            left.SourceFlow == right.SourceFlow &&
            StringComparer.Ordinal.Equals(left.AdapterDeviceId, right.AdapterDeviceId);

        public int GetHashCode(RawKsCandidate value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.AdapterDeviceId),
                value.SourceFlow);
    }

    private sealed class NativeCallException(string operation, int nativeHResult) : InvalidOperationException($"{operation} failed with HRESULT 0x{nativeHResult:X8}.")
    {
        public string Operation { get; } = operation;

        public int NativeHResult { get; } = nativeHResult;
    }
}
