namespace QuickPods.Spike.CoreAudio.Tests;

public sealed class VolumeMathTests
{
    [Theory]
    [InlineData(-5d, 0f)]
    [InlineData(0d, 0f)]
    [InlineData(42d, 0.42f)]
    [InlineData(100d, 1f)]
    [InlineData(105d, 1f)]
    public void PercentToScalarClampsToEndpointRange(double percent, float expected)
    {
        Assert.Equal(expected, VolumeMath.PercentToScalar(percent), precision: 3);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ScalarToPercentRejectsNonFiniteValues(float scalar)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VolumeMath.ScalarToPercent(scalar));
    }

    [Theory]
    [InlineData(-0.5f, 0)]
    [InlineData(0.424f, 42)]
    [InlineData(0.426f, 43)]
    [InlineData(1.5f, 100)]
    public void ScalarToDisplayPercentRoundsAndClamps(float scalar, int expected)
    {
        Assert.Equal(expected, VolumeMath.ScalarToDisplayPercent(scalar));
    }
}
