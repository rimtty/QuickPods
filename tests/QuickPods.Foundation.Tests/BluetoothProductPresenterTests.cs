using QuickPods.Core.Models;
using QuickPods.Presentation;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class BluetoothProductPresenterTests
{
    private static readonly BluetoothDeviceKey DeviceA = new("bt-presenter-a");
    private static readonly BluetoothDeviceKey DeviceB = new("bt-presenter-b");

    [Fact]
    public void SameNameRowsRemainDistinctAndConnectedDefaultSelectsDisconnect()
    {
        BluetoothAudioCatalogSnapshot catalog = Catalog(
            generation: 3,
            Device(DeviceA, "Same name", isSelected: true) with
            {
                ConnectionState = BluetoothConnectionState.Connected,
                DefaultOutputState = DefaultOutputState.Default,
            },
            Device(DeviceB, "Same name", isSelected: false));

        BluetoothProductPresentation view = BluetoothProductPresenter.Project(
            catalog,
            BluetoothOperationSnapshot.Idle,
            isRefreshing: false);

        Assert.Equal(2, view.Devices.Length);
        Assert.NotEqual(view.Devices[0].DeviceKey, view.Devices[1].DeviceKey);
        Assert.Equal("接続済み・既定", view.Devices[0].StatusText);
        Assert.Equal(ProductPrimaryActionKind.Disconnect, view.PrimaryAction);
        Assert.True(view.IsPrimaryActionEnabled);
    }

    [Fact]
    public void ConnectedNotDefaultOffersRetryAndSoundRecovery()
    {
        BluetoothAudioCatalogSnapshot catalog = Catalog(
            generation: 7,
            Device(DeviceA, "Headphones", isSelected: true) with
            {
                ConnectionState = BluetoothConnectionState.Connected,
                DefaultOutputState = DefaultOutputState.NotDefault,
            });
        var operation = new BluetoothOperationSnapshot(
            Revision: 4,
            new BluetoothOperationTarget(DeviceA, 7),
            BluetoothRequestedAction.Connect,
            QuickPodsOperation.None,
            BluetoothOperationOutcome.ConnectedNotDefault,
            BluetoothConnectionState.Connected,
            DefaultOutputState.Failed,
            QuickPodsErrorCode.DefaultOutputSwitchFailed);

        BluetoothProductPresentation view = BluetoothProductPresenter.Project(
            catalog,
            operation,
            isRefreshing: false);

        Assert.Equal(ProductPrimaryActionKind.MakeDefault, view.PrimaryAction);
        Assert.Equal("既定の出力に設定", view.PrimaryActionText);
        Assert.True(view.ShowSoundRecovery);
        Assert.NotNull(view.ErrorMessage);
    }

    [Fact]
    public void RefreshRetainsUnsupportedRowsButDisablesTheirFallbackAction()
    {
        BluetoothAudioCatalogSnapshot catalog = Catalog(
            generation: 2,
            Device(DeviceA, "Unsupported", isSelected: true) with
            {
                Capability = BluetoothDeviceCapability.SettingsOnly,
            });

        BluetoothProductPresentation ready = BluetoothProductPresenter.Project(
            catalog,
            BluetoothOperationSnapshot.Idle,
            isRefreshing: false);
        BluetoothProductPresentation refreshing = BluetoothProductPresenter.Project(
            catalog,
            BluetoothOperationSnapshot.Idle,
            isRefreshing: true);

        Assert.Equal(ProductPrimaryActionKind.OpenBluetoothSettings, ready.PrimaryAction);
        Assert.True(ready.IsPrimaryActionEnabled);
        Assert.Single(refreshing.Devices);
        Assert.False(refreshing.IsPrimaryActionEnabled);
        Assert.False(refreshing.Devices[0].IsEnabled);
    }

    [Fact]
    public void ResultFromAnOlderInventoryGenerationCannotOverwriteCurrentView()
    {
        BluetoothAudioCatalogSnapshot catalog = Catalog(
            generation: 9,
            Device(DeviceA, "Current", isSelected: true));
        var staleOperation = new BluetoothOperationSnapshot(
            Revision: 10,
            new BluetoothOperationTarget(DeviceA, 8),
            BluetoothRequestedAction.Connect,
            QuickPodsOperation.None,
            BluetoothOperationOutcome.Succeeded,
            BluetoothConnectionState.Connected,
            DefaultOutputState.Default,
            null);

        BluetoothProductPresentation view = BluetoothProductPresenter.Project(
            catalog,
            staleOperation,
            isRefreshing: false);

        Assert.Equal("未接続", Assert.Single(view.Devices).StatusText);
        Assert.Equal(ProductPrimaryActionKind.Connect, view.PrimaryAction);
    }

    private static BluetoothAudioCatalogSnapshot Catalog(
        long generation,
        params BluetoothAudioDeviceDescriptor[] devices)
    {
        BluetoothAudioDeviceDescriptor? selected = devices.FirstOrDefault(device => device.IsSelected);
        return new(
            generation,
            Revision: generation,
            selected?.DeviceKey,
            SelectedDevicePresent: selected is not null,
            [.. devices]);
    }

    private static BluetoothAudioDeviceDescriptor Device(
        BluetoothDeviceKey key,
        string name,
        bool isSelected) =>
        new(
            key,
            name,
            BluetoothAudioKind.Headphones,
            BluetoothConnectionState.Disconnected,
            DefaultOutputState.NotApplicable,
            BluetoothDeviceCapability.DirectControl,
            [BluetoothAudioProfile.Stereo],
            isSelected);
}
