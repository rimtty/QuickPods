using QuickPods.Core;
using QuickPods.Core.Models;
using QuickPods.Core.Ports;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class AudioControllerTests
{
    [Fact]
    public async Task RapidPreviewCommitsLatestValueWithBoundedWrites()
    {
        var port = new FakeAudioPort();
        await using var controller = new AudioController(port, TimeSpan.FromMilliseconds(20));
        await controller.InitializeAsync();

        for (int value = 0; value < 100; value++)
        {
            controller.PreviewVolume(value);
        }

        await controller.CommitVolumeAsync(73);

        Assert.InRange(port.VolumeWrites.Count, 1, 2);
        Assert.Equal(73, port.VolumeWrites[^1]);
        Assert.Equal(73, controller.State.VolumePercent);
    }

    [Fact]
    public async Task RetiredGenerationCannotOverwriteCurrentEndpoint()
    {
        var port = new FakeAudioPort();
        await using var controller = new AudioController(port);
        await controller.InitializeAsync();

        port.Raise(new AudioState(AudioCapability.Available, 60, false, "New output")
        {
            Generation = 2,
        });
        port.Raise(new AudioState(AudioCapability.Available, 10, true, "Retired output")
        {
            Generation = 1,
        });

        Assert.Equal("New output", controller.State.EndpointDisplayName);
        Assert.Equal(60, controller.State.VolumePercent);
        Assert.False(controller.State.IsMuted);
    }

    [Fact]
    public async Task ExternalNotificationUpdatesStateWithoutWritingBack()
    {
        var port = new FakeAudioPort();
        await using var controller = new AudioController(port);
        await controller.InitializeAsync();

        port.Raise(new AudioState(AudioCapability.Available, 25, true, "Output")
        {
            Generation = 1,
        });

        Assert.Equal(25, controller.State.VolumePercent);
        Assert.True(controller.State.IsMuted);
        Assert.Empty(port.VolumeWrites);
        Assert.Equal(0, port.MuteWrites);
    }

    [Fact]
    public async Task SelfNotificationCannotRollbackOptimisticPreview()
    {
        var port = new FakeAudioPort();
        await using var controller = new AudioController(port, TimeSpan.FromMilliseconds(100));
        await controller.InitializeAsync();

        controller.PreviewVolume(80);
        port.Raise(
            new AudioState(AudioCapability.Available, 42, false, "Output")
            {
                Generation = 1,
            },
            isSelfOriginated: true);

        Assert.Equal(80, controller.State.VolumePercent);
    }

    [Fact]
    public async Task ObservedWriteFailureDoesNotFailShutdownAgain()
    {
        var port = new FakeAudioPort { FailVolumeWrites = true };
        var controller = new AudioController(port, TimeSpan.FromMilliseconds(1));
        await controller.InitializeAsync();

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.CommitVolumeAsync(25).AsTask());
        await controller.DisposeAsync();

        Assert.Equal("Simulated volume failure.", failure.Message);
    }

    private sealed class FakeAudioPort : IAudioEndpointPort
    {
        private AudioState state = new(
            AudioCapability.Available,
            42,
            false,
            "Output")
        {
            Generation = 1,
        };

        public event EventHandler<AudioStateChangedEventArgs>? StateChanged;

        public List<int> VolumeWrites { get; } = [];

        public int MuteWrites { get; private set; }

        public bool FailVolumeWrites { get; init; }

        public ValueTask<AudioState> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(state);

        public ValueTask<AudioState> SetVolumeAsync(
            int volumePercent,
            CancellationToken cancellationToken)
        {
            if (FailVolumeWrites)
            {
                throw new InvalidOperationException("Simulated volume failure.");
            }

            VolumeWrites.Add(volumePercent);
            state = state with { VolumePercent = volumePercent };
            return ValueTask.FromResult(state);
        }

        public ValueTask<AudioState> SetMuteAsync(
            bool isMuted,
            CancellationToken cancellationToken)
        {
            MuteWrites++;
            state = state with { IsMuted = isMuted };
            return ValueTask.FromResult(state);
        }

        public void Raise(AudioState newState, bool isSelfOriginated = false)
        {
            state = newState;
            StateChanged?.Invoke(this, new AudioStateChangedEventArgs(newState, isSelfOriginated));
        }
    }
}
