namespace QuickPods.Spike.CoreAudio.Tests;

public sealed class GenerationAndStatisticsTests
{
    [Fact]
    public void GenerationGateRejectsCallbacksFromPreviousBinding()
    {
        var gate = new GenerationGate();

        long first = gate.Advance();
        long second = gate.Advance();

        Assert.False(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }

    [Fact]
    public void PercentileUsesNearestRank()
    {
        double percentile = Statistics.Percentile(Enumerable.Range(1, 100).Select(value => (double)value), 0.95d);

        Assert.Equal(95d, percentile);
    }

    [Theory]
    [InlineData(new long[] { 1, 2, 3 }, true)]
    [InlineData(new long[] { 1, 1, 2 }, true)]
    [InlineData(new long[] { 1, 2, 2 }, true)]
    [InlineData(new long[] { 3, 2, 1 }, false)]
    [InlineData(new long[] { 1, 3, 2 }, false)]
    [InlineData(new long[] { 1, 1, 1 }, false)]
    [InlineData(new long[] { 1 }, false)]
    public void NonDecreasingGrowthAllowsPlateausButRequiresNetGrowth(long[] samples, bool expected)
    {
        Assert.Equal(expected, Statistics.HasNonDecreasingGrowth(samples));
    }
}
