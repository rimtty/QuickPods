namespace QuickPods.Spike.CoreAudio.Tests;

public sealed class VolumeNotificationBufferTests
{
    [Fact]
    public async Task BurstRetainsLatestNotificationInBoundedMailbox()
    {
        var buffer = new VolumeNotificationBuffer();
        for (int index = 0; index < 10_000; index++)
        {
            buffer.Publish(new VolumeNotification(
                Generation: 1,
                EventContext: Guid.Empty,
                IsMuted: false,
                VolumeScalar: index / 10_000f,
                CallbackTimestamp: index));
        }

        VolumeNotification latest = await buffer.WaitForAsync(
            _ => true,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(9_999, latest.CallbackTimestamp);
        Assert.Equal(0.9999f, latest.VolumeScalar, precision: 4);
    }
}
