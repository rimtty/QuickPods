using System.Collections.Immutable;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;

namespace QuickPods.Core;

public sealed class BluetoothCatalogController : IAsyncDisposable
{
    private readonly IBluetoothAudioCatalogPort catalog;
    private readonly IBluetoothSelectionStore selectionStore;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private readonly SemaphoreSlim selectionGate = new(1, 1);
    private readonly object stateLock = new();
    private BluetoothAudioCatalogSnapshot state = BluetoothAudioCatalogSnapshot.Empty;
    private BluetoothDeviceKey? selectedDevice;
    private long nextInventoryGeneration;
    private bool initialized;
    private bool disposed;

    public BluetoothCatalogController(
        IBluetoothAudioCatalogPort catalog,
        IBluetoothSelectionStore selectionStore)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
    }

    public event EventHandler<BluetoothAudioCatalogSnapshot>? StateChanged;

    public BluetoothAudioCatalogSnapshot State
    {
        get
        {
            lock (stateLock)
            {
                return state;
            }
        }
    }

    public async ValueTask<BluetoothAudioCatalogSnapshot> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!initialized)
            {
                selectedDevice = await selectionStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                initialized = true;
            }
        }
        finally
        {
            initializationGate.Release();
        }

        return await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BluetoothAudioCatalogSnapshot> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        long requestedGeneration = Interlocked.Increment(ref nextInventoryGeneration);
        BluetoothAudioCatalogObservation observation =
            await catalog.DiscoverAsync(requestedGeneration, cancellationToken).ConfigureAwait(false);
        if (observation.InventoryGeneration != requestedGeneration)
        {
            throw new InvalidDataException("The Bluetooth catalog observation generation did not match its request.");
        }

        BluetoothAudioCatalogSnapshot? published = null;
        lock (stateLock)
        {
            if (requestedGeneration == Volatile.Read(ref nextInventoryGeneration) &&
                requestedGeneration > state.InventoryGeneration)
            {
                published = BluetoothAudioCatalogBuilder.Build(
                    observation,
                    selectedDevice,
                    checked(state.Revision + 1));
                state = published;
            }
        }

        if (published is not null)
        {
            StateChanged?.Invoke(this, published);
        }

        return State;
    }

    public async ValueTask<BluetoothAudioCatalogSnapshot> SelectAsync(
        BluetoothDeviceKey deviceKey,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureInitialized();
        await selectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (stateLock)
            {
                if (!state.Devices.Any(device => device.DeviceKey == deviceKey))
                {
                    throw new ArgumentException(
                        "The selected Bluetooth audio device is not present in the current catalog.",
                        nameof(deviceKey));
                }
            }

            await selectionStore.SaveAsync(deviceKey, cancellationToken).ConfigureAwait(false);

            BluetoothAudioCatalogSnapshot published;
            lock (stateLock)
            {
                selectedDevice = deviceKey;
                published = BluetoothAudioCatalogBuilder.WithSelection(
                    state,
                    selectedDevice,
                    checked(state.Revision + 1));
                state = published;
            }

            StateChanged?.Invoke(this, published);
            return published;
        }
        finally
        {
            selectionGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            initializationGate.Dispose();
            selectionGate.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void EnsureInitialized()
    {
        if (!initialized)
        {
            throw new InvalidOperationException("InitializeAsync must complete before catalog operations.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}

internal static class BluetoothAudioCatalogBuilder
{
    internal static BluetoothAudioCatalogSnapshot Build(
        BluetoothAudioCatalogObservation observation,
        BluetoothDeviceKey? selectedDevice,
        long revision)
    {
        ImmutableArray<BluetoothAudioDeviceDescriptor> devices = [.. observation.Endpoints
            .Where(IsCandidate)
            .GroupBy(endpoint => endpoint.DeviceKey)
            .Where(group => group.Any(endpoint => endpoint.Direction == BluetoothEndpointDirection.Render))
            .Select(group => BuildDevice(group, selectedDevice))];
        return CreateSnapshot(
            observation.InventoryGeneration,
            revision,
            selectedDevice,
            devices);
    }

    internal static BluetoothAudioCatalogSnapshot WithSelection(
        BluetoothAudioCatalogSnapshot current,
        BluetoothDeviceKey? selectedDevice,
        long revision)
    {
        ImmutableArray<BluetoothAudioDeviceDescriptor> devices = [.. current.Devices.Select(
            device => device with { IsSelected = device.DeviceKey == selectedDevice })];
        return CreateSnapshot(
            current.InventoryGeneration,
            revision,
            selectedDevice,
            devices);
    }

    private static BluetoothAudioCatalogSnapshot CreateSnapshot(
        long inventoryGeneration,
        long revision,
        BluetoothDeviceKey? selectedDevice,
        ImmutableArray<BluetoothAudioDeviceDescriptor> devices)
    {
        ImmutableArray<BluetoothAudioDeviceDescriptor> ordered = [.. devices
            .OrderByDescending(device => device.IsSelected)
            .ThenByDescending(device => device.ConnectionState == BluetoothConnectionState.Connected)
            .ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.DeviceKey.Value, StringComparer.Ordinal)];
        bool selectedPresent = selectedDevice is not null && ordered.Any(device => device.IsSelected);
        return new(
            inventoryGeneration,
            revision,
            selectedDevice,
            selectedPresent,
            ordered);
    }

    private static BluetoothAudioDeviceDescriptor BuildDevice(
        IGrouping<BluetoothDeviceKey, BluetoothAudioEndpointEvidence> group,
        BluetoothDeviceKey? selectedDevice)
    {
        BluetoothAudioEndpointEvidence[] endpoints = [.. group];
        string displayName = endpoints
            .Select(endpoint => (endpoint.DisplayName ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "Bluetooth audio";
        BluetoothAudioKind[] kinds = [.. endpoints
            .Select(endpoint => endpoint.Kind)
            .Where(kind => kind != BluetoothAudioKind.Unknown)
            .Distinct()];
        ImmutableArray<BluetoothAudioProfile> profiles = [.. endpoints
            .Select(endpoint => endpoint.Profile)
            .Distinct()
            .Order()];
        ImmutableArray<byte> iconPng = endpoints
            .Select(endpoint => endpoint.IconPng)
            .FirstOrDefault(icon => !icon.IsDefaultOrEmpty);
        BluetoothAudioEndpointEvidence[] renderEndpoints = [.. endpoints.Where(
            endpoint => endpoint.Direction == BluetoothEndpointDirection.Render)];
        BluetoothConnectionState connection = ResolveConnection(renderEndpoints);
        bool connected = connection == BluetoothConnectionState.Connected;
        bool isDefault = renderEndpoints.Any(endpoint =>
            endpoint.Profile == BluetoothAudioProfile.Stereo &&
            endpoint.IsConsoleDefault &&
            endpoint.IsMultimediaDefault);
        return new(
            group.Key,
            displayName,
            kinds.Length == 1 ? kinds[0] : BluetoothAudioKind.Unknown,
            connection,
            connected
                ? isDefault ? DefaultOutputState.Default : DefaultOutputState.NotDefault
                : DefaultOutputState.NotApplicable,
            ResolveCapability(endpoints),
            profiles,
            group.Key == selectedDevice)
        {
            IconPng = iconPng.IsDefault ? [] : iconPng,
        };
    }

    private static BluetoothConnectionState ResolveConnection(
        IReadOnlyList<BluetoothAudioEndpointEvidence> renderEndpoints)
    {
        if (renderEndpoints.Any(endpoint => endpoint.Availability == BluetoothEndpointAvailability.Active))
        {
            return BluetoothConnectionState.Connected;
        }

        if (renderEndpoints.All(endpoint => endpoint.Availability == BluetoothEndpointAvailability.Disabled))
        {
            return BluetoothConnectionState.Unavailable;
        }

        if (renderEndpoints.All(endpoint => endpoint.Availability is
                BluetoothEndpointAvailability.NotPresent or BluetoothEndpointAvailability.Unplugged))
        {
            return BluetoothConnectionState.Disconnected;
        }

        return BluetoothConnectionState.Unknown;
    }

    private static BluetoothDeviceCapability ResolveCapability(
        IReadOnlyList<BluetoothAudioEndpointEvidence> endpoints)
    {
        if (endpoints.Any(endpoint => endpoint.Capability == BluetoothDeviceCapability.OwnershipUnknown))
        {
            return BluetoothDeviceCapability.OwnershipUnknown;
        }

        if (endpoints.Any(endpoint => endpoint.Capability == BluetoothDeviceCapability.DirectControl))
        {
            return BluetoothDeviceCapability.DirectControl;
        }

        if (endpoints.All(endpoint => endpoint.Capability == BluetoothDeviceCapability.SettingsOnly))
        {
            return BluetoothDeviceCapability.SettingsOnly;
        }

        return BluetoothDeviceCapability.TemporarilyUnavailable;
    }

    private static bool IsCandidate(BluetoothAudioEndpointEvidence endpoint) =>
        endpoint.IsBluetooth &&
        endpoint.IsPaired &&
        !string.IsNullOrWhiteSpace(endpoint.DeviceKey.Value);
}
