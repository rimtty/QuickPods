namespace QuickPods.Spike.CoreAudio.Tests;

public sealed class CoreAudioOptionsTests
{
    [Fact]
    public void ExerciseDefaultsToBoundedGateValues()
    {
        CoreAudioOptions options = CoreAudioOptions.Parse(["exercise", "--confirm-playback-stopped"]);

        Assert.Equal(CoreAudioCommand.Exercise, options.Command);
        Assert.Equal(1000, options.Iterations);
        Assert.Equal(1d, options.DeltaPercent);
        Assert.True(options.PlaybackStoppedConfirmed);
    }

    [Fact]
    public void PulseRequiresExplicitTarget()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => CoreAudioOptions.Parse(["pulse"]));

        Assert.Contains("--percent", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("-1")]
    [InlineData("101")]
    public void PulseRejectsUnsafeTarget(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => CoreAudioOptions.Parse(["pulse", "--percent", value]));
    }

    [Fact]
    public void ExerciseRejectsDeltaAboveFivePercent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CoreAudioOptions.Parse(["exercise", "--delta-percent", "5.1"]));
    }
}
