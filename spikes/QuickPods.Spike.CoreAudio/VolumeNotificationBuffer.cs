using System.Threading.Channels;

namespace QuickPods.Spike.CoreAudio;

internal sealed class VolumeNotificationBuffer
{
    private readonly Channel<VolumeNotification> _channel = Channel.CreateBounded<VolumeNotification>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    public void Publish(VolumeNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        _ = _channel.Writer.TryWrite(notification);
    }

    public void Drain()
    {
        while (_channel.Reader.TryRead(out _))
        {
        }
    }

    public async Task<VolumeNotification> WaitForAsync(
        Func<VolumeNotification, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        try
        {
            while (await _channel.Reader.WaitToReadAsync(linkedCancellation.Token).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out VolumeNotification? notification))
                {
                    if (predicate(notification))
                    {
                        return notification;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No matching Core Audio notification arrived within {timeout.TotalSeconds:F1} seconds.");
        }

        throw new InvalidOperationException("The volume notification channel closed unexpectedly.");
    }
}
