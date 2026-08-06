using QuickPods.Core;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class VolumeMathTests
{
    [Theory]
    [InlineData(-10, 0f)]
    [InlineData(0, 0f)]
    [InlineData(50, 0.5f)]
    [InlineData(100, 1f)]
    [InlineData(120, 1f)]
    public void PercentToScalarClampsToWindowsRange(int percent, float expected)
    {
        Assert.Equal(expected, VolumeMath.PercentToScalar(percent));
    }

    [Theory]
    [InlineData(-0.2f, 0)]
    [InlineData(0.5f, 50)]
    [InlineData(1.2f, 100)]
    public void ScalarToPercentClampsToDisplayRange(float scalar, int expected)
    {
        Assert.Equal(expected, VolumeMath.ScalarToPercent(scalar));
    }
}
