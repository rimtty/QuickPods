using System.IO;
using QuickPods.Core.Models;
using QuickPods.Infrastructure.Settings;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task SettingsRoundTripPreservesSelectionAndOptions()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "QuickPods.Foundation.Tests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");

        try
        {
            var store = new JsonSettingsStore<QuickPodsSettings>(path);
            var expected = new QuickPodsSettings(
                new BluetoothDeviceKey("opaque-device-key"),
                StartWithWindows: true,
                PreferNativeTaskbarSurface: false,
                DisplayMode: QuickPodsDisplayMode.TrayOnly,
                SetConnectedDeviceAsDefault: false,
                MouseWheelStepPercent: 5,
                Theme: QuickPodsThemeMode.Light,
                ConfirmBluetoothDisconnect: true);

            await store.SaveAsync(expected);
            QuickPodsSettings? actual = await store.LoadAsync();

            Assert.Equal(expected, actual);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LegacySettingsReceiveSafePhaseFiveDefaults()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "QuickPods.Foundation.Tests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");

        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "SelectedDevice": null,
                  "StartWithWindows": false,
                  "PreferNativeTaskbarSurface": true
                }
                """);
            using var settings = new JsonBluetoothSelectionStore(
                new JsonSettingsStore<QuickPodsSettings>(path));

            QuickPodsSettings migrated = await settings.LoadSettingsAsync();

            Assert.Equal(1, migrated.SchemaVersion);
            Assert.Equal(QuickPodsDisplayMode.Auto, migrated.DisplayMode);
            Assert.True(migrated.SetConnectedDeviceAsDefault);
            Assert.Equal(2, migrated.MouseWheelStepPercent);
            Assert.Equal(QuickPodsThemeMode.System, migrated.Theme);
            Assert.False(migrated.ConfirmBluetoothDisconnect);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task BluetoothSelectionStorePreservesUnrelatedOptions()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "QuickPods.Foundation.Tests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");

        try
        {
            var settings = new JsonSettingsStore<QuickPodsSettings>(path);
            await settings.SaveAsync(new QuickPodsSettings(
                null,
                StartWithWindows: true,
                PreferNativeTaskbarSurface: false,
                DisplayMode: QuickPodsDisplayMode.TrayOnly,
                SetConnectedDeviceAsDefault: false,
                MouseWheelStepPercent: 10,
                Theme: QuickPodsThemeMode.Dark,
                ConfirmBluetoothDisconnect: true));
            using var selection = new JsonBluetoothSelectionStore(settings);

            await selection.SaveAsync(new BluetoothDeviceKey("bt-device"));
            _ = await selection.UpdateSettingsAsync(
                current => current with { StartWithWindows = false });
            QuickPodsSettings actual = Assert.IsType<QuickPodsSettings>(await settings.LoadAsync());

            Assert.Equal(new BluetoothDeviceKey("bt-device"), actual.SelectedDevice);
            Assert.False(actual.StartWithWindows);
            Assert.False(actual.PreferNativeTaskbarSurface);
            Assert.Equal(QuickPodsDisplayMode.TrayOnly, actual.DisplayMode);
            Assert.False(actual.SetConnectedDeviceAsDefault);
            Assert.Equal(10, actual.MouseWheelStepPercent);
            Assert.Equal(QuickPodsThemeMode.Dark, actual.Theme);
            Assert.True(actual.ConfirmBluetoothDisconnect);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
