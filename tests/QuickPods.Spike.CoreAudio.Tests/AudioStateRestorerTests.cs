namespace QuickPods.Spike.CoreAudio.Tests;

public sealed class AudioStateRestorerTests
{
    [Fact]
    public void RestoreMutesBeforeVolumeAndRestoresOriginalMuteLast()
    {
        var endpoint = new RecordingAudioStateAccess(observedVolume: 0.42f, observedMuted: false);

        bool restored = AudioStateRestorer.RestoreAndVerify(endpoint, 0.42f, originalMuted: false);

        Assert.True(restored);
        Assert.Equal(
            ["mute:true", "volume:0.420", "mute:false", "get-volume", "get-mute"],
            endpoint.Operations);
    }

    [Fact]
    public void RestoreDoesNotReportSuccessWhenVerificationDiffers()
    {
        var endpoint = new RecordingAudioStateAccess(observedVolume: 0.5f, observedMuted: false);

        Assert.False(AudioStateRestorer.RestoreAndVerify(endpoint, 0.42f, originalMuted: false));
    }

    private sealed class RecordingAudioStateAccess(
        float observedVolume,
        bool observedMuted) : IAudioStateAccess
    {
        public List<string> Operations { get; } = [];

        public bool GetMuted()
        {
            Operations.Add("get-mute");
            return observedMuted;
        }

        public float GetVolumeScalar()
        {
            Operations.Add("get-volume");
            return observedVolume;
        }

        public void SetMuted(bool isMuted)
        {
            Operations.Add($"mute:{isMuted.ToString().ToLowerInvariant()}");
        }

        public void SetVolumeScalar(float scalar)
        {
            Operations.Add(FormattableString.Invariant($"volume:{scalar:F3}"));
        }
    }
}
