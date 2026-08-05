using QuickPods.Spike.BluetoothKs.Catalog;
using QuickPods.Spike.BluetoothKs.Interop;
using QuickPods.Spike.BluetoothKs.Observation;

namespace QuickPods.Spike.BluetoothKs.Tests;

public sealed class BluetoothAudioCatalogTests
{
    [Fact]
    public void BuilderGroupsProfilesByContainerAndKeepsSameNameDevicesDistinct()
    {
        IReadOnlyList<BluetoothAudioCatalogDevice> devices = BluetoothAudioCatalogBuilder.Build(
        [
            Endpoint("container-a", "a-stereo", BluetoothAudioProfile.Stereo, NativeDataFlow.Render, BluetoothEndpointState.Active),
            Endpoint("container-a", "a-hfp-render", BluetoothAudioProfile.HandsFree, NativeDataFlow.Render, BluetoothEndpointState.Unplugged),
            Endpoint("container-a", "a-hfp-capture", BluetoothAudioProfile.HandsFree, NativeDataFlow.Capture, BluetoothEndpointState.Unplugged),
            Endpoint("container-b", "b-stereo", BluetoothAudioProfile.Stereo, NativeDataFlow.Render, BluetoothEndpointState.NotPresent),
            Endpoint("container-unpaired", "u-stereo", BluetoothAudioProfile.Stereo, NativeDataFlow.Render, BluetoothEndpointState.Active) with { IsPaired = false },
            Endpoint("container-wired", "w-stereo", BluetoothAudioProfile.Stereo, NativeDataFlow.Render, BluetoothEndpointState.Active) with { IsBluetooth = false },
        ]);

        Assert.Equal(2, devices.Count);
        Assert.All(devices, device => Assert.Equal("Same display name", device.DisplayName));
        BluetoothAudioCatalogDevice first = Assert.Single(
            devices,
            device => device.ContainerKey == "container-a");
        Assert.Equal(BluetoothCatalogConnectionState.Connected, first.ConnectionState);
        Assert.Equal(
            [BluetoothAudioProfile.Stereo, BluetoothAudioProfile.HandsFree],
            first.Profiles);
        Assert.Equal(3, first.EndpointKeys.Count);
        BluetoothAudioCatalogDevice second = Assert.Single(
            devices,
            device => device.ContainerKey == "container-b");
        Assert.Equal(BluetoothCatalogConnectionState.Disconnected, second.ConnectionState);
    }

    [Fact]
    public void SelectionPersistsAcrossReadOnlyRefreshAndMissingDevice()
    {
        var state = new BluetoothAudioCatalogState("container-a");
        BluetoothAudioCatalogSnapshot initial = state.Refresh(
        [
            Endpoint("container-a", "a-stereo", BluetoothAudioProfile.Stereo, NativeDataFlow.Render, BluetoothEndpointState.Unplugged),
            Endpoint("container-b", "b-stereo", BluetoothAudioProfile.Stereo, NativeDataFlow.Render, BluetoothEndpointState.Active),
        ]);

        Assert.Equal("container-a", initial.SelectedContainerKey);
        Assert.True(initial.SelectedDevicePresent);
        Assert.Equal("container-a", initial.Devices[0].ContainerKey);

        BluetoothAudioCatalogSnapshot selected = state.Select("container-b");
        Assert.Equal("container-b", selected.SelectedContainerKey);
        Assert.Equal("container-b", selected.Devices[0].ContainerKey);

        BluetoothAudioCatalogSnapshot missing = state.Refresh(
        [
            Endpoint("container-a", "a-stereo", BluetoothAudioProfile.Stereo, NativeDataFlow.Render, BluetoothEndpointState.Unplugged),
        ]);
        Assert.Equal("container-b", missing.SelectedContainerKey);
        Assert.False(missing.SelectedDevicePresent);

        BluetoothAudioCatalogSnapshot empty = state.Refresh([]);
        Assert.Empty(empty.Devices);
        Assert.Equal("container-b", empty.SelectedContainerKey);
        Assert.False(empty.SelectedDevicePresent);
        Assert.True(
            initial.Revision < selected.Revision &&
            selected.Revision < missing.Revision &&
            missing.Revision < empty.Revision);
    }

    private static BluetoothAudioEndpointCandidate Endpoint(
        string containerKey,
        string endpointKey,
        BluetoothAudioProfile profile,
        NativeDataFlow flow,
        BluetoothEndpointState state)
    {
        return new BluetoothAudioEndpointCandidate(
            containerKey,
            endpointKey,
            "Same display name",
            BluetoothAudioDeviceKind.Headphones,
            profile,
            flow,
            state,
            IsPaired: true,
            IsBluetooth: true);
    }
}
