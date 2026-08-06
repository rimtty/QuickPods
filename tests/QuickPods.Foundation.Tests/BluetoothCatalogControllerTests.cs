using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Windows.Bluetooth;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class BluetoothCatalogControllerTests
{
    private static readonly BluetoothDeviceKey DeviceA = new("bt-device-a");
    private static readonly BluetoothDeviceKey DeviceB = new("bt-device-b");

    [Fact]
    public void ContainerKeyIsStableOpaqueAndContainerSpecific()
    {
        Guid firstContainer = new("11223344-5566-7788-99AA-BBCCDDEEFF00");

        BluetoothDeviceKey first = BluetoothDeviceKeyFactory.Create(firstContainer);
        BluetoothDeviceKey repeated = BluetoothDeviceKeyFactory.Create(firstContainer);
        BluetoothDeviceKey other = BluetoothDeviceKeyFactory.Create(
            new Guid("11223344-5566-7788-99AA-BBCCDDEEFF01"));

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, other);
        Assert.StartsWith("bt-", first.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(firstContainer.ToString("D"), first.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsBindingRegistryRequiresTheExactNewestInventoryGeneration()
    {
        var registry = new WindowsBluetoothBindingRegistry();
        var binding = new WindowsBluetoothDeviceBinding(
            DeviceA,
            new Guid("11223344-5566-7788-99AA-BBCCDDEEFF00"),
            [new(
                "synthetic-endpoint",
                BluetoothEndpointDirection.Render,
                BluetoothAudioProfile.Stereo,
                BluetoothEndpointAvailability.NotPresent,
                [])],
            HasAmbiguousAdapterOwnership: false);

        Assert.True(registry.Publish(2, [binding]));
        Assert.False(registry.Publish(1, []));
        Assert.False(registry.TryResolve(new BluetoothOperationTarget(DeviceA, 1), out _));
        Assert.True(registry.TryResolve(new BluetoothOperationTarget(DeviceA, 2), out var resolved));
        Assert.Same(binding, resolved);
    }

    [Fact]
    public void SharedKsCandidateMarksEveryOwningContainerAmbiguous()
    {
        WindowsBluetoothEndpointBinding Endpoint(params string[] adapters) => new(
            "synthetic-endpoint",
            BluetoothEndpointDirection.Render,
            BluetoothAudioProfile.Stereo,
            BluetoothEndpointAvailability.NotPresent,
            [.. adapters]);
        var first = new WindowsBluetoothDeviceBinding(
            DeviceA,
            Guid.NewGuid(),
            [Endpoint("shared-adapter", "first-only")],
            HasAmbiguousAdapterOwnership: false);
        var second = new WindowsBluetoothDeviceBinding(
            DeviceB,
            Guid.NewGuid(),
            [Endpoint("shared-adapter", "second-only")],
            HasAmbiguousAdapterOwnership: false);

        WindowsBluetoothDeviceBinding[] result =
            [.. WindowsBluetoothAudioCatalogPort.MarkAmbiguousAdapterOwnership([first, second])];

        Assert.All(result, binding => Assert.True(binding.HasAmbiguousAdapterOwnership));
    }

    [Fact]
    public async Task CatalogAggregatesProfilesAndKeepsDeviceFaultsIndependent()
    {
        var port = new QueueCatalogPort();
        port.Enqueue(
            Endpoint(DeviceA, "Same name", BluetoothAudioProfile.Stereo, BluetoothEndpointDirection.Render,
                BluetoothEndpointAvailability.Active, BluetoothDeviceCapability.DirectControl) with
            {
                IsConsoleDefault = true,
                IsMultimediaDefault = true,
            },
            Endpoint(DeviceA, "Same name", BluetoothAudioProfile.HandsFree, BluetoothEndpointDirection.Capture,
                BluetoothEndpointAvailability.Active, BluetoothDeviceCapability.DirectControl),
            Endpoint(DeviceB, "Same name", BluetoothAudioProfile.Stereo, BluetoothEndpointDirection.Render,
                BluetoothEndpointAvailability.NotPresent, BluetoothDeviceCapability.OwnershipUnknown),
            Endpoint(new("unpaired"), "Ignored", BluetoothAudioProfile.Stereo,
                BluetoothEndpointDirection.Render, BluetoothEndpointAvailability.Active,
                BluetoothDeviceCapability.DirectControl) with
            { IsPaired = false },
            Endpoint(new("wired"), "Ignored", BluetoothAudioProfile.Stereo,
                BluetoothEndpointDirection.Render, BluetoothEndpointAvailability.Active,
                BluetoothDeviceCapability.DirectControl) with
            { IsBluetooth = false });
        var selection = new MemorySelectionStore();
        await using var controller = new BluetoothCatalogController(port, selection);

        BluetoothAudioCatalogSnapshot state = await controller.InitializeAsync();

        Assert.Equal(2, state.Devices.Length);
        BluetoothAudioDeviceDescriptor connected = Assert.Single(
            state.Devices,
            device => device.DeviceKey == DeviceA);
        Assert.Equal(BluetoothConnectionState.Connected, connected.ConnectionState);
        Assert.Equal(DefaultOutputState.Default, connected.DefaultOutputState);
        Assert.Equal(BluetoothDeviceCapability.DirectControl, connected.Capability);
        Assert.Equal(
            [BluetoothAudioProfile.Stereo, BluetoothAudioProfile.HandsFree],
            connected.Profiles.AsEnumerable());
        BluetoothAudioDeviceDescriptor isolatedFault = Assert.Single(
            state.Devices,
            device => device.DeviceKey == DeviceB);
        Assert.Equal(BluetoothDeviceCapability.OwnershipUnknown, isolatedFault.Capability);

        port.Enqueue();
        Assert.Empty((await controller.RefreshAsync()).Devices);
    }

    [Fact]
    public async Task SelectionPersistsWithoutAnyMutationPort()
    {
        var port = new QueueCatalogPort();
        port.Enqueue(
            Endpoint(DeviceA, "A", BluetoothAudioProfile.Stereo, BluetoothEndpointDirection.Render,
                BluetoothEndpointAvailability.NotPresent, BluetoothDeviceCapability.DirectControl),
            Endpoint(DeviceB, "B", BluetoothAudioProfile.Stereo, BluetoothEndpointDirection.Render,
                BluetoothEndpointAvailability.Active, BluetoothDeviceCapability.DirectControl));
        var selection = new MemorySelectionStore();
        await using var controller = new BluetoothCatalogController(port, selection);
        await controller.InitializeAsync();

        BluetoothAudioCatalogSnapshot state = await controller.SelectAsync(DeviceA);

        Assert.Equal(DeviceA, selection.Selected);
        Assert.Equal(DeviceA, state.SelectedDeviceKey);
        Assert.True(state.SelectedDevicePresent);
        Assert.Equal(DeviceA, state.Devices[0].DeviceKey);
        Assert.Equal(1, port.DiscoveryCalls);
    }

    [Fact]
    public async Task LateRefreshCannotOverwriteNewerInventory()
    {
        var port = new ControlledCatalogPort();
        var selection = new MemorySelectionStore(DeviceA);
        await using var controller = new BluetoothCatalogController(port, selection);
        Task<BluetoothAudioCatalogSnapshot> initialization = controller.InitializeAsync().AsTask();
        port.Complete(1, Endpoint(DeviceA, "A", BluetoothAudioProfile.Stereo,
            BluetoothEndpointDirection.Render, BluetoothEndpointAvailability.NotPresent,
            BluetoothDeviceCapability.DirectControl));
        BluetoothAudioCatalogSnapshot initialized = await initialization;
        Assert.True(initialized.SelectedDevicePresent);

        Task<BluetoothAudioCatalogSnapshot> older = controller.RefreshAsync().AsTask();
        Task<BluetoothAudioCatalogSnapshot> newer = controller.RefreshAsync().AsTask();
        port.Complete(3, Endpoint(DeviceB, "B", BluetoothAudioProfile.Stereo,
            BluetoothEndpointDirection.Render, BluetoothEndpointAvailability.Active,
            BluetoothDeviceCapability.DirectControl));
        await newer;
        port.Complete(2, Endpoint(DeviceA, "stale", BluetoothAudioProfile.Stereo,
            BluetoothEndpointDirection.Render, BluetoothEndpointAvailability.Active,
            BluetoothDeviceCapability.DirectControl));
        await older;

        BluetoothAudioCatalogSnapshot state = controller.State;
        Assert.Equal(3, state.InventoryGeneration);
        Assert.Equal(DeviceB, Assert.Single(state.Devices).DeviceKey);
        Assert.Equal(DeviceA, state.SelectedDeviceKey);
        Assert.False(state.SelectedDevicePresent);
    }

    private static BluetoothAudioEndpointEvidence Endpoint(
        BluetoothDeviceKey key,
        string name,
        BluetoothAudioProfile profile,
        BluetoothEndpointDirection direction,
        BluetoothEndpointAvailability availability,
        BluetoothDeviceCapability capability) =>
        new(
            key,
            name,
            BluetoothAudioKind.Headphones,
            profile,
            direction,
            availability,
            capability,
            IsPaired: true,
            IsBluetooth: true);

    private sealed class QueueCatalogPort : IBluetoothAudioCatalogPort
    {
        private readonly Queue<IReadOnlyList<BluetoothAudioEndpointEvidence>> responses = new();

        public int DiscoveryCalls { get; private set; }

        public void Enqueue(params BluetoothAudioEndpointEvidence[] endpoints) => responses.Enqueue(endpoints);

        public ValueTask<BluetoothAudioCatalogObservation> DiscoverAsync(
            long inventoryGeneration,
            CancellationToken cancellationToken)
        {
            DiscoveryCalls++;
            return ValueTask.FromResult(new BluetoothAudioCatalogObservation(
                inventoryGeneration,
                responses.Dequeue()));
        }
    }

    private sealed class ControlledCatalogPort : IBluetoothAudioCatalogPort
    {
        private readonly Dictionary<long, TaskCompletionSource<BluetoothAudioCatalogObservation>> requests = [];

        public ValueTask<BluetoothAudioCatalogObservation> DiscoverAsync(
            long inventoryGeneration,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<BluetoothAudioCatalogObservation>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Add(inventoryGeneration, completion);
            return new(completion.Task.WaitAsync(cancellationToken));
        }

        public void Complete(long generation, params BluetoothAudioEndpointEvidence[] endpoints) =>
            requests[generation].SetResult(new(generation, endpoints));
    }

    private sealed class MemorySelectionStore(BluetoothDeviceKey? selected = null) : IBluetoothSelectionStore
    {
        public BluetoothDeviceKey? Selected { get; private set; } = selected;

        public ValueTask<BluetoothDeviceKey?> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Selected);

        public ValueTask SaveAsync(
            BluetoothDeviceKey? selectedDevice,
            CancellationToken cancellationToken = default)
        {
            Selected = selectedDevice;
            return ValueTask.CompletedTask;
        }
    }
}
