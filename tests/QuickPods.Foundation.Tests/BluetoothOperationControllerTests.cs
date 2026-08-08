using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class BluetoothOperationControllerTests
{
    private static readonly BluetoothDeviceKey DeviceA = new("bt-operation-a");
    private static readonly BluetoothDeviceKey DeviceB = new("bt-operation-b");

    [Fact]
    public async Task SuccessfulSwitchDisconnectsPreviouslyConnectedAudioDevice()
    {
        await using BluetoothCatalogController catalog = await CreateSwitchCatalogAsync(
            BluetoothDeviceCapability.DirectControl);
        var bluetooth = new FakeBluetoothOperationPort();
        var defaultOutput = new FakeDefaultOutputOperationPort(
            new(DefaultOutputState.Default, RequestSubmitted: true, BluetoothMutationFailure.None));
        using var controller = CreateController(catalog, bluetooth, defaultOutput);
        var operations = new List<QuickPodsOperation>();
        controller.StateChanged += (_, state) => operations.Add(state.Operation);

        BluetoothOperationSnapshot result = await controller.ConnectSelectedAsync();

        Assert.Equal(BluetoothOperationOutcome.Succeeded, result.Outcome);
        Assert.Equal(DefaultOutputState.Default, result.DefaultOutputState);
        Assert.Equal([DeviceB], bluetooth.ConnectTargets);
        Assert.Equal([DeviceA], bluetooth.DisconnectTargets);
        Assert.Equal([DeviceB], defaultOutput.Targets);
        Assert.Contains(QuickPodsOperation.DisconnectingOtherDevices, operations);
    }

    [Fact]
    public async Task DefaultOutputFailureKeepsPreviouslyConnectedAudioDeviceConnected()
    {
        await using BluetoothCatalogController catalog = await CreateSwitchCatalogAsync(
            BluetoothDeviceCapability.DirectControl);
        var bluetooth = new FakeBluetoothOperationPort();
        var defaultOutput = new FakeDefaultOutputOperationPort(
            new(DefaultOutputState.Failed, RequestSubmitted: true, BluetoothMutationFailure.Rejected));
        using var controller = CreateController(catalog, bluetooth, defaultOutput);

        BluetoothOperationSnapshot result = await controller.ConnectSelectedAsync();

        Assert.Equal(BluetoothOperationOutcome.ConnectedNotDefault, result.Outcome);
        Assert.Empty(bluetooth.DisconnectTargets);
    }

    [Fact]
    public async Task ConnectWithoutDefaultSwitchKeepsPreviouslyConnectedAudioDeviceConnected()
    {
        await using BluetoothCatalogController catalog = await CreateSwitchCatalogAsync(
            BluetoothDeviceCapability.DirectControl);
        var bluetooth = new FakeBluetoothOperationPort();
        var defaultOutput = new FakeDefaultOutputOperationPort(
            new(DefaultOutputState.Default, RequestSubmitted: true, BluetoothMutationFailure.None));
        using var controller = CreateController(catalog, bluetooth, defaultOutput);

        BluetoothOperationSnapshot result = await controller.ConnectSelectedAsync(
            setConnectedDeviceAsDefault: false);

        Assert.Equal(BluetoothOperationOutcome.Succeeded, result.Outcome);
        Assert.Equal(DefaultOutputState.NotDefault, result.DefaultOutputState);
        Assert.Empty(bluetooth.DisconnectTargets);
        Assert.Empty(defaultOutput.Targets);
    }

    [Fact]
    public async Task UnsupportedPreviousDeviceLeavesHonestSwitchWarning()
    {
        await using BluetoothCatalogController catalog = await CreateSwitchCatalogAsync(
            BluetoothDeviceCapability.SettingsOnly);
        var bluetooth = new FakeBluetoothOperationPort();
        var defaultOutput = new FakeDefaultOutputOperationPort(
            new(DefaultOutputState.Default, RequestSubmitted: true, BluetoothMutationFailure.None));
        using var controller = CreateController(catalog, bluetooth, defaultOutput);

        BluetoothOperationSnapshot result = await controller.ConnectSelectedAsync();

        Assert.Equal(BluetoothOperationOutcome.Succeeded, result.Outcome);
        Assert.Equal(QuickPodsErrorCode.OtherBluetoothDevicesStillConnected, result.Error);
        Assert.Empty(bluetooth.DisconnectTargets);
    }

    [Fact]
    public async Task PreviousDeviceDisconnectFailureLeavesHonestSwitchWarning()
    {
        await using BluetoothCatalogController catalog = await CreateSwitchCatalogAsync(
            BluetoothDeviceCapability.DirectControl);
        var bluetooth = new FakeBluetoothOperationPort
        {
            Disconnect = (_, _) => ValueTask.FromResult(new BluetoothDeviceOperationResult(
                BluetoothConnectionState.Connected,
                RequestSubmitted: true,
                BluetoothMutationFailure.Rejected)),
        };
        var defaultOutput = new FakeDefaultOutputOperationPort(
            new(DefaultOutputState.Default, RequestSubmitted: true, BluetoothMutationFailure.None));
        using var controller = CreateController(catalog, bluetooth, defaultOutput);

        BluetoothOperationSnapshot result = await controller.ConnectSelectedAsync();

        Assert.Equal(BluetoothOperationOutcome.Succeeded, result.Outcome);
        Assert.Equal(DefaultOutputState.Default, result.DefaultOutputState);
        Assert.Equal(QuickPodsErrorCode.OtherBluetoothDevicesStillConnected, result.Error);
        Assert.Equal([DeviceA], bluetooth.DisconnectTargets);
    }

    [Fact]
    public async Task DefaultFailurePreservesConfirmedConnectionWithoutRetry()
    {
        await using BluetoothCatalogController catalog = await CreateCatalogAsync(
            BluetoothDeviceCapability.DirectControl,
            includeSecondDevice: false);
        var bluetooth = new FakeBluetoothOperationPort();
        var defaultOutput = new FakeDefaultOutputOperationPort(
            new(DefaultOutputState.Failed, RequestSubmitted: true, BluetoothMutationFailure.Rejected));
        using var controller = CreateController(catalog, bluetooth, defaultOutput);

        BluetoothOperationSnapshot result = await controller.ConnectSelectedAsync();

        Assert.Equal(BluetoothOperationOutcome.ConnectedNotDefault, result.Outcome);
        Assert.Equal(BluetoothConnectionState.Connected, result.ConnectionState);
        Assert.Equal(DefaultOutputState.Failed, result.DefaultOutputState);
        Assert.Equal(1, bluetooth.ConnectCalls);
        Assert.Equal(1, defaultOutput.Calls);
    }

    [Fact]
    public async Task ConnectCanExplicitlySkipDefaultOutputMutation()
    {
        await using BluetoothCatalogController catalog = await CreateCatalogAsync(
            BluetoothDeviceCapability.DirectControl,
            includeSecondDevice: false);
        var bluetooth = new FakeBluetoothOperationPort();
        var defaultOutput = new FakeDefaultOutputOperationPort(
            new(DefaultOutputState.Default, RequestSubmitted: true, BluetoothMutationFailure.None));
        using var controller = CreateController(catalog, bluetooth, defaultOutput);

        BluetoothOperationSnapshot result = await controller.ConnectSelectedAsync(
            setConnectedDeviceAsDefault: false);

        Assert.Equal(BluetoothOperationOutcome.Succeeded, result.Outcome);
        Assert.Equal(BluetoothConnectionState.Connected, result.ConnectionState);
        Assert.Equal(DefaultOutputState.NotDefault, result.DefaultOutputState);
        Assert.Equal(1, bluetooth.ConnectCalls);
        Assert.Equal(0, defaultOutput.Calls);
    }

    [Fact]
    public async Task SelectionChangeSupersedesConnectAndSkipsDefaultOutput()
    {
        await using BluetoothCatalogController catalog = await CreateCatalogAsync(
            BluetoothDeviceCapability.DirectControl,
            includeSecondDevice: true);
        var completion = new TaskCompletionSource<BluetoothDeviceOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bluetooth = new FakeBluetoothOperationPort
        {
            Connect = (_, _) => new ValueTask<BluetoothDeviceOperationResult>(completion.Task),
        };
        var defaultOutput = new FakeDefaultOutputOperationPort(
            new(DefaultOutputState.Default, RequestSubmitted: true, BluetoothMutationFailure.None));
        using var controller = CreateController(catalog, bluetooth, defaultOutput);

        ValueTask<BluetoothOperationSnapshot> pending = controller.ConnectSelectedAsync();
        await catalog.SelectAsync(DeviceB);
        completion.SetResult(new(
            BluetoothConnectionState.Connected,
            RequestSubmitted: true,
            BluetoothMutationFailure.None));
        BluetoothOperationSnapshot result = await pending;

        Assert.Equal(BluetoothOperationOutcome.Superseded, result.Outcome);
        Assert.Equal(0, defaultOutput.Calls);
        Assert.Equal(DeviceA, result.Target?.DeviceKey);
    }

    [Fact]
    public async Task SelectionChangeBeforeSubmissionSendsNoMutation()
    {
        await using BluetoothCatalogController catalog = await CreateCatalogAsync(
            BluetoothDeviceCapability.DirectControl,
            includeSecondDevice: true);
        var bluetooth = new FakeBluetoothOperationPort();
        using var controller = new BluetoothOperationController(
            catalog,
            bluetooth,
            new FakeDefaultOutputOperationPort(
                new(DefaultOutputState.Default, RequestSubmitted: true, BluetoothMutationFailure.None)),
            new PassThroughOperationGate());
        controller.StateChanged += (_, state) =>
        {
            if (state.Outcome == BluetoothOperationOutcome.InProgress)
            {
                catalog.SelectAsync(DeviceB).AsTask().GetAwaiter().GetResult();
            }
        };

        BluetoothOperationSnapshot result = await controller.ConnectSelectedAsync();

        Assert.Equal(BluetoothOperationOutcome.Superseded, result.Outcome);
        Assert.Equal(0, bluetooth.ConnectCalls);
    }

    [Fact]
    public async Task UnsupportedDeviceSendsNoMutation()
    {
        await using BluetoothCatalogController catalog = await CreateCatalogAsync(
            BluetoothDeviceCapability.SettingsOnly,
            includeSecondDevice: false);
        var bluetooth = new FakeBluetoothOperationPort();
        var defaultOutput = new FakeDefaultOutputOperationPort(
            new(DefaultOutputState.Default, RequestSubmitted: true, BluetoothMutationFailure.None));
        using var controller = CreateController(catalog, bluetooth, defaultOutput);

        BluetoothOperationSnapshot result = await controller.ConnectSelectedAsync();

        Assert.Equal(BluetoothOperationOutcome.Unsupported, result.Outcome);
        Assert.Equal(0, bluetooth.ConnectCalls);
        Assert.Equal(0, defaultOutput.Calls);
    }

    [Fact]
    public async Task ConcurrentDisconnectRequestsAreGloballySerialized()
    {
        await using BluetoothCatalogController catalog = await CreateCatalogAsync(
            BluetoothDeviceCapability.DirectControl,
            includeSecondDevice: false);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bluetooth = new FakeBluetoothOperationPort
        {
            Disconnect = async (_, _) =>
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
                return new(
                    BluetoothConnectionState.Disconnected,
                    RequestSubmitted: true,
                    BluetoothMutationFailure.None);
            },
        };
        using var controller = new BluetoothOperationController(
            catalog,
            bluetooth,
            new FakeDefaultOutputOperationPort(
                new(DefaultOutputState.Default, RequestSubmitted: true, BluetoothMutationFailure.None)),
            new PassThroughOperationGate());

        ValueTask<BluetoothOperationSnapshot> first = controller.DisconnectSelectedAsync();
        await firstEntered.Task;
        ValueTask<BluetoothOperationSnapshot> second = controller.DisconnectSelectedAsync();
        await Task.Yield();

        Assert.Equal(1, bluetooth.DisconnectCalls);
        releaseFirst.SetResult();
        BluetoothOperationSnapshot[] results = [await first, await second];
        Assert.All(results, result => Assert.Equal(BluetoothOperationOutcome.Succeeded, result.Outcome));
        Assert.Equal(2, bluetooth.DisconnectCalls);
        Assert.Equal(1, bluetooth.MaximumConcurrentCalls);
    }

    private static async Task<BluetoothCatalogController> CreateCatalogAsync(
        BluetoothDeviceCapability capability,
        bool includeSecondDevice)
    {
        BluetoothAudioEndpointEvidence[] endpoints = includeSecondDevice
            ? [Endpoint(DeviceA, "Device A", capability), Endpoint(DeviceB, "Device B", capability)]
            : [Endpoint(DeviceA, "Device A", capability)];
        var controller = new BluetoothCatalogController(
            new FakeCatalogPort(endpoints),
            new FakeSelectionStore(DeviceA));
        await controller.InitializeAsync();
        return controller;
    }

    private static async Task<BluetoothCatalogController> CreateSwitchCatalogAsync(
        BluetoothDeviceCapability previousDeviceCapability)
    {
        var controller = new BluetoothCatalogController(
            new FakeCatalogPort(
            [
                Endpoint(DeviceA, "Previous device", previousDeviceCapability),
                Endpoint(
                    DeviceB,
                    "New device",
                    BluetoothDeviceCapability.DirectControl,
                    BluetoothEndpointAvailability.NotPresent),
            ]),
            new FakeSelectionStore(DeviceB));
        await controller.InitializeAsync();
        return controller;
    }

    private static BluetoothOperationController CreateController(
        BluetoothCatalogController catalog,
        IBluetoothDeviceOperationPort bluetooth,
        IDefaultOutputOperationPort defaultOutput) =>
        new(catalog, bluetooth, defaultOutput, new PassThroughOperationGate());

    private static BluetoothAudioEndpointEvidence Endpoint(
        BluetoothDeviceKey key,
        string name,
        BluetoothDeviceCapability capability,
        BluetoothEndpointAvailability availability = BluetoothEndpointAvailability.Active) =>
        new(
            key,
            name,
            BluetoothAudioKind.Headphones,
            BluetoothAudioProfile.Stereo,
            BluetoothEndpointDirection.Render,
            availability,
            capability,
            IsPaired: true,
            IsBluetooth: true);

    private sealed class FakeCatalogPort(IEnumerable<BluetoothAudioEndpointEvidence> endpoints)
        : IBluetoothAudioCatalogPort
    {
        public ValueTask<BluetoothAudioCatalogObservation> DiscoverAsync(
            long inventoryGeneration,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new BluetoothAudioCatalogObservation(inventoryGeneration, endpoints));
    }

    private sealed class FakeSelectionStore(BluetoothDeviceKey? selected) : IBluetoothSelectionStore
    {
        public ValueTask<BluetoothDeviceKey?> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(selected);

        public ValueTask SaveAsync(
            BluetoothDeviceKey? selectedDevice,
            CancellationToken cancellationToken = default)
        {
            selected = selectedDevice;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeBluetoothOperationPort : IBluetoothDeviceOperationPort
    {
        private int concurrentCalls;
        private int maximumConcurrentCalls;

        public Func<BluetoothOperationTarget, CancellationToken, ValueTask<BluetoothDeviceOperationResult>>
            Connect
        { get; init; } = (_, _) => ValueTask.FromResult(new BluetoothDeviceOperationResult(
                BluetoothConnectionState.Connected,
                RequestSubmitted: true,
                BluetoothMutationFailure.None));

        public Func<BluetoothOperationTarget, CancellationToken, ValueTask<BluetoothDeviceOperationResult>>
            Disconnect
        { get; init; } = (_, _) => ValueTask.FromResult(new BluetoothDeviceOperationResult(
                BluetoothConnectionState.Disconnected,
                RequestSubmitted: true,
                BluetoothMutationFailure.None));

        public int ConnectCalls { get; private set; }

        public int DisconnectCalls { get; private set; }

        public int MaximumConcurrentCalls => Volatile.Read(ref maximumConcurrentCalls);

        public List<BluetoothDeviceKey> ConnectTargets { get; } = [];

        public List<BluetoothDeviceKey> DisconnectTargets { get; } = [];

        public async ValueTask<BluetoothDeviceOperationResult> ConnectAsync(
            BluetoothOperationTarget target,
            CancellationToken cancellationToken)
        {
            ConnectCalls++;
            ConnectTargets.Add(target.DeviceKey);
            return await InvokeAsync(Connect, target, cancellationToken);
        }

        public async ValueTask<BluetoothDeviceOperationResult> DisconnectAsync(
            BluetoothOperationTarget target,
            CancellationToken cancellationToken)
        {
            DisconnectCalls++;
            DisconnectTargets.Add(target.DeviceKey);
            return await InvokeAsync(Disconnect, target, cancellationToken);
        }

        private async ValueTask<BluetoothDeviceOperationResult> InvokeAsync(
            Func<BluetoothOperationTarget, CancellationToken, ValueTask<BluetoothDeviceOperationResult>> operation,
            BluetoothOperationTarget target,
            CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref concurrentCalls);
            InterlockedExtensions.Max(ref maximumConcurrentCalls, current);
            try
            {
                return await operation(target, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref concurrentCalls);
            }
        }
    }

    private sealed class FakeDefaultOutputOperationPort(DefaultOutputOperationResult result)
        : IDefaultOutputOperationPort
    {
        public DefaultOutputCapability Capability { get; init; } = DefaultOutputCapability.Supported;

        public int Calls { get; private set; }

        public List<BluetoothDeviceKey> Targets { get; } = [];

        public ValueTask<DefaultOutputOperationResult> MakeDefaultAsync(
            BluetoothOperationTarget target,
            CancellationToken cancellationToken)
        {
            Calls++;
            Targets.Add(target.DeviceKey);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PassThroughOperationGate : IBluetoothOperationGatePort
    {
        public async ValueTask<BluetoothOperationAdmissionResult<T>> RunAsync<T>(
            Func<ValueTask<T>> operation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(
                BluetoothOperationAdmissionStatus.Executed,
                await operation());
        }
    }

    private static class InterlockedExtensions
    {
        internal static void Max(ref int location, int value)
        {
            int current;
            do
            {
                current = Volatile.Read(ref location);
                if (current >= value)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref location, value, current) != current);
        }
    }
}
