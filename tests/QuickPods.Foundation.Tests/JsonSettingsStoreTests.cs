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
                PreferNativeTaskbarSurface: false);

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
                PreferNativeTaskbarSurface: false));
            using var selection = new JsonBluetoothSelectionStore(settings);

            await selection.SaveAsync(new BluetoothDeviceKey("bt-device"));
            QuickPodsSettings actual = Assert.IsType<QuickPodsSettings>(await settings.LoadAsync());

            Assert.Equal(new BluetoothDeviceKey("bt-device"), actual.SelectedDevice);
            Assert.True(actual.StartWithWindows);
            Assert.False(actual.PreferNativeTaskbarSurface);
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
