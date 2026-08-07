using System.Collections.Concurrent;
using System.Collections.Immutable;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace QuickPods.Windows.Bluetooth;

internal sealed class WindowsBluetoothDeviceIconReader
{
    private static readonly TimeSpan IconTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<Guid, ImmutableArray<byte>> cache = new();

    public async ValueTask<ImmutableArray<byte>> ReadAsync(
        Guid containerId,
        string deviceInstanceId,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(containerId, out ImmutableArray<byte> cached))
        {
            return cached;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(IconTimeout);
        ImmutableArray<byte> icon;
        try
        {
            icon = await ReadFromDeviceContainerAsync(containerId, timeout.Token)
                .ConfigureAwait(false);
            if (icon.IsDefaultOrEmpty)
            {
                icon = await ReadFromDeviceAsync(deviceInstanceId, timeout.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            icon = [];
        }
        catch
        {
            icon = [];
        }

        if (!icon.IsDefaultOrEmpty)
        {
            cache.TryAdd(containerId, icon);
        }

        return icon;
    }

    private static async Task<ImmutableArray<byte>> ReadFromDeviceContainerAsync(
        Guid containerId,
        CancellationToken cancellationToken)
    {
        DeviceInformation information = await DeviceInformation.CreateFromIdAsync(
                containerId.ToString("B"),
                [],
                DeviceInformationKind.DeviceContainer)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        return await ReadThumbnailAsync(information, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ImmutableArray<byte>> ReadFromDeviceAsync(
        string deviceInstanceId,
        CancellationToken cancellationToken)
    {
        DeviceInformation information = await DeviceInformation.CreateFromIdAsync(
                deviceInstanceId,
                [],
                DeviceInformationKind.Device)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        return await ReadThumbnailAsync(information, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ImmutableArray<byte>> ReadThumbnailAsync(
        DeviceInformation information,
        CancellationToken cancellationToken)
    {
        using DeviceThumbnail thumbnail = await information.GetThumbnailAsync()
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (thumbnail.Size == 0 || thumbnail.Size > int.MaxValue)
        {
            return [];
        }

        uint length = checked((uint)thumbnail.Size);
        using var reader = new DataReader(thumbnail.GetInputStreamAt(0));
        uint loaded = await reader.LoadAsync(length)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (loaded == 0)
        {
            return [];
        }

        byte[] bytes = new byte[loaded];
        reader.ReadBytes(bytes);
        return [.. bytes];
    }
}
