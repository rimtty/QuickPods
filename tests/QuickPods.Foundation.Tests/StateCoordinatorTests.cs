using System.Collections.Immutable;
using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class StateCoordinatorTests
{
    private static readonly BluetoothDeviceKey AirPods = new("device-key-airpods");

    [Fact]
    public async Task RefreshKeepsCapabilitiesIndependent()
    {
        using StateCoordinator coordinator = CreateCoordinator(
            BluetoothDeviceCapability.SettingsOnly,
            DefaultOutputCapability.PolicyUnavailable,
            TaskbarCapability.FloatingOnly);

        QuickPodsState state = await coordinator.RefreshAsync();

        Assert.Equal(AudioCapability.Available, state.Audio.Capability);
        Assert.Equal(TaskbarCapability.FloatingOnly, state.TaskbarCapability);
        Assert.Equal(DefaultOutputCapability.PolicyUnavailable, state.DefaultOutputCapability);
        Assert.Equal(BluetoothDeviceCapability.SettingsOnly, state.BluetoothDevices[0].Capability);
    }

    [Fact]
    public async Task UnsupportedDeviceDoesNotInvokeConnect()
    {
        var bluetooth = new FakeBluetoothPort(BluetoothDeviceCapability.SettingsOnly);
        using StateCoordinator coordinator = CreateCoordinator(
            bluetooth,
            new FakeDefaultOutputPort(DefaultOutputCapability.Supported, true),
            TaskbarCapability.NativeAvailable);
        await coordinator.RefreshAsync();
        await coordinator.SelectAsync(AirPods);

        QuickPodsState state = await coordinator.ConnectSelectedAsync();

        Assert.Equal(0, bluetooth.ConnectCalls);
        Assert.Equal(QuickPodsErrorCode.BluetoothDriverUnsupported, state.Error);
    }

    [Fact]
    public async Task DefaultFailurePreservesBluetoothConnection()
    {
        var bluetooth = new FakeBluetoothPort(BluetoothDeviceCapability.DirectControl);
        using StateCoordinator coordinator = CreateCoordinator(
            bluetooth,
            new FakeDefaultOutputPort(DefaultOutputCapability.Supported, false),
            TaskbarCapability.NativeAvailable);
        await coordinator.RefreshAsync();
        await coordinator.SelectAsync(AirPods);

        QuickPodsState state = await coordinator.ConnectSelectedAsync();
        BluetoothDeviceState selected = Assert.IsType<BluetoothDeviceState>(state.FindSelectedDevice());

        Assert.Equal(BluetoothConnectionState.Connected, selected.ConnectionState);
        Assert.Equal(DefaultOutputState.Failed, selected.DefaultOutputState);
        Assert.Equal(QuickPodsErrorCode.DefaultOutputSwitchFailed, state.Error);
    }

    [Fact]
    public async Task SelectionDoesNotMutateOsState()
    {
        var bluetooth = new FakeBluetoothPort(BluetoothDeviceCapability.DirectControl);
        using StateCoordinator coordinator = CreateCoordinator(
            bluetooth,
            new FakeDefaultOutputPort(DefaultOutputCapability.Supported, true),
            TaskbarCapability.NativeAvailable);
        await coordinator.RefreshAsync();

        QuickPodsState state = await coordinator.SelectAsync(AirPods);

        Assert.Equal(AirPods, state.SelectedDevice);
        Assert.Equal(0, bluetooth.ConnectCalls);
        Assert.Equal(0, bluetooth.DisconnectCalls);
    }

    private static StateCoordinator CreateCoordinator(
        BluetoothDeviceCapability bluetoothCapability,
        DefaultOutputCapability defaultOutputCapability,
        TaskbarCapability taskbarCapability) =>
        CreateCoordinator(
            new FakeBluetoothPort(bluetoothCapability),
            new FakeDefaultOutputPort(defaultOutputCapability, false),
            taskbarCapability);

    private static StateCoordinator CreateCoordinator(
        FakeBluetoothPort bluetooth,
        FakeDefaultOutputPort defaultOutput,
        TaskbarCapability taskbarCapability) =>
        new(
            new FakeAudioPort(),
            bluetooth,
            defaultOutput,
            new FakeTaskbarPort(taskbarCapability));

    private sealed class FakeAudioPort : IAudioEndpointPort
    {
        public ValueTask<AudioState> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AudioState(AudioCapability.Available, 42, false, "Output"));
    }

    private sealed class FakeBluetoothPort : IBluetoothAudioPort
    {
        private readonly BluetoothDeviceCapability capability;

        public FakeBluetoothPort(BluetoothDeviceCapability capability)
        {
            this.capability = capability;
        }

        public int ConnectCalls { get; private set; }

        public int DisconnectCalls { get; private set; }

        public ValueTask<ImmutableArray<BluetoothDeviceState>> GetDevicesAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(ImmutableArray.Create(
                new BluetoothDeviceState(
                    AirPods,
                    "AirPods Pro",
                    capability,
                    BluetoothConnectionState.Disconnected,
                    DefaultOutputState.NotApplicable)));

        public ValueTask<BluetoothConnectionState> ConnectAsync(
            BluetoothDeviceKey device,
            CancellationToken cancellationToken)
        {
            ConnectCalls++;
            return ValueTask.FromResult(BluetoothConnectionState.Connected);
        }

        public ValueTask<BluetoothConnectionState> DisconnectAsync(
            BluetoothDeviceKey device,
            CancellationToken cancellationToken)
        {
            DisconnectCalls++;
            return ValueTask.FromResult(BluetoothConnectionState.Disconnected);
        }
    }

    private sealed class FakeDefaultOutputPort : IDefaultOutputPort
    {
        private readonly bool result;

        public FakeDefaultOutputPort(DefaultOutputCapability capability, bool result)
        {
            Capability = capability;
            this.result = result;
        }

        public DefaultOutputCapability Capability { get; }

        public ValueTask<bool> MakeDefaultAsync(
            BluetoothDeviceKey device,
            CancellationToken cancellationToken) => ValueTask.FromResult(result);
    }

    private sealed class FakeTaskbarPort : ITaskbarPresentationPort
    {
        public FakeTaskbarPort(TaskbarCapability capability)
        {
            Capability = capability;
        }

        public TaskbarCapability Capability { get; }
    }
}
