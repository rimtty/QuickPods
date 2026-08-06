using QuickPods.Core.Models;
using QuickPods.Core.Ports;

namespace QuickPods.Infrastructure.Settings;

public sealed class UninstallCleanupService
{
    private readonly IStartupRegistration startupRegistration;
    private readonly ISettingsStore<QuickPodsSettings> settingsStore;

    public UninstallCleanupService(
        IStartupRegistration startupRegistration,
        ISettingsStore<QuickPodsSettings> settingsStore)
    {
        this.startupRegistration = startupRegistration ??
            throw new ArgumentNullException(nameof(startupRegistration));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async ValueTask ExecuteAsync(
        bool settingsFileExists,
        CancellationToken cancellationToken = default)
    {
        await startupRegistration.SetEnabledAsync(false, cancellationToken).ConfigureAwait(false);
        if (!settingsFileExists)
        {
            return;
        }

        QuickPodsSettings settings =
            await settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false) ??
            QuickPodsSettings.Default;
        await settingsStore.SaveAsync(
            settings.Normalize() with { StartWithWindows = false },
            cancellationToken).ConfigureAwait(false);
    }
}
