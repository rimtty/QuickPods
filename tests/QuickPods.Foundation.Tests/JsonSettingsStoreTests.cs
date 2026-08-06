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
}
