using QuickPods.Core.Models;
using QuickPods.Core.Ports;

namespace QuickPods.Infrastructure.Settings;

public sealed class JsonBluetoothSelectionStore : IBluetoothSelectionStore, IDisposable
{
    private readonly ISettingsStore<QuickPodsSettings> settings;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;

    public JsonBluetoothSelectionStore(ISettingsStore<QuickPodsSettings> settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public SettingsRecoveryInfo? LastRecovery =>
        (settings as JsonSettingsStore<QuickPodsSettings>)?.LastRecovery;

    public async ValueTask<BluetoothDeviceKey?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        QuickPodsSettings current = await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        return current.SelectedDevice;
    }

    public async ValueTask SaveAsync(
        BluetoothDeviceKey? selectedDevice,
        CancellationToken cancellationToken = default)
    {
        _ = await UpdateSettingsAsync(
            current => current with { SelectedDevice = selectedDevice },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<QuickPodsSettings> LoadSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            QuickPodsSettings? loaded =
                await settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            QuickPodsSettings current = loaded ?? QuickPodsSettings.Default;
            if (loaded is null && LastRecovery is not null)
            {
                await settings.SaveAsync(current, cancellationToken).ConfigureAwait(false);
            }

            return current.Normalize();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<QuickPodsSettings> UpdateSettingsAsync(
        Func<QuickPodsSettings, QuickPodsSettings> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            QuickPodsSettings current =
                await settings.LoadAsync(cancellationToken).ConfigureAwait(false) ??
                QuickPodsSettings.Default;
            current = current.Normalize();
            QuickPodsSettings updated = update(current) ??
                throw new InvalidOperationException("The settings update returned null.");
            updated = updated.Normalize();
            await settings.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            gate.Dispose();
        }
    }
}
