using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using QuickPods.Infrastructure.Settings;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class UninstallCleanupServiceTests
{
    [Fact]
    public async Task CleanupDisablesStartupAndPersistedIntentWithoutDeletingUserData()
    {
        var startup = new FakeStartupRegistration();
        var settings = new FakeSettingsStore
        {
            Value = QuickPodsSettings.Default with
            {
                SelectedDevice = new BluetoothDeviceKey("opaque-selection"),
                StartWithWindows = true,
                Theme = QuickPodsThemeMode.Light,
            },
        };
        var cleanup = new UninstallCleanupService(startup, settings);

        await cleanup.ExecuteAsync(settingsFileExists: true);

        Assert.False(startup.Enabled);
        Assert.False(settings.Value!.StartWithWindows);
        Assert.Equal(new BluetoothDeviceKey("opaque-selection"), settings.Value.SelectedDevice);
        Assert.Equal(QuickPodsThemeMode.Light, settings.Value.Theme);
        Assert.Equal(1, settings.SaveCount);
    }

    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        internal bool Enabled { get; private set; } = true;

        public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Enabled);

        public ValueTask SetEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            Enabled = enabled;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore<QuickPodsSettings>
    {
        internal QuickPodsSettings? Value { get; set; }

        internal int SaveCount { get; private set; }

        public ValueTask<QuickPodsSettings?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Value);

        public ValueTask SaveAsync(
            QuickPodsSettings value,
            CancellationToken cancellationToken = default)
        {
            Value = value;
            SaveCount++;
            return ValueTask.CompletedTask;
        }
    }
}
