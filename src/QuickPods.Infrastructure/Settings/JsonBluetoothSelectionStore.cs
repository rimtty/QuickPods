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

    public async ValueTask<BluetoothDeviceKey?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            QuickPodsSettings? current = await settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            return current?.SelectedDevice;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask SaveAsync(
        BluetoothDeviceKey? selectedDevice,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            QuickPodsSettings current =
                await settings.LoadAsync(cancellationToken).ConfigureAwait(false) ??
                QuickPodsSettings.Default;
            await settings.SaveAsync(
                current with { SelectedDevice = selectedDevice },
                cancellationToken).ConfigureAwait(false);
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
