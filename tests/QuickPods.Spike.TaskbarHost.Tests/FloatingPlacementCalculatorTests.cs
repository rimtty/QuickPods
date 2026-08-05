using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class FloatingPlacementCalculatorTests
{
    [Theory]
    [InlineData(96u, 300, 40, 8, 1770, 2112)]
    [InlineData(120u, 375, 50, 10, 1732, 2100)]
    [InlineData(144u, 450, 60, 12, 1695, 2088)]
    [InlineData(192u, 600, 80, 16, 1620, 2064)]
    public void Calculate_UsesFixedDipMetricsAtSupportedDpi(
        uint dpi,
        int expectedWidth,
        int expectedHeight,
        int expectedMargin,
        int expectedLeft,
        int expectedTop)
    {
        var workArea = new PixelRect(0, 0, 3840, 2160);

        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(workArea, dpi));

        PixelRect bounds = AssertAvailable(result);
        Assert.Equal(expectedWidth, bounds.Width);
        Assert.Equal(expectedHeight, bounds.Height);
        Assert.Equal(expectedLeft, bounds.Left);
        Assert.Equal(expectedTop, bounds.Top);
        Assert.Equal(expectedMargin, workArea.Bottom - bounds.Bottom);
        Assert.True(workArea.Contains(bounds));
    }

    [Fact]
    public void Calculate_SupportsNegativeVirtualDesktopCoordinates()
    {
        var workArea = new PixelRect(-1920, -1080, 0, 0);

        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(workArea, 96));

        Assert.Equal(
            new PixelRect(-1110, -48, -810, -8),
            AssertAvailable(result));
    }

    [Fact]
    public void Calculate_CentersOnPriorNativeBoundsWhenPreferenceFits()
    {
        var workArea = new PixelRect(0, 0, 1920, 1040);
        var priorNativeBounds = new PixelRect(200, 1000, 390, 1040);

        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(workArea, 96, priorNativeBounds));

        PixelRect bounds = AssertAvailable(result);
        Assert.Equal(new PixelRect(145, 992, 445, 1032), bounds);
        Assert.Equal(
            (long)priorNativeBounds.Left + priorNativeBounds.Right,
            (long)bounds.Left + bounds.Right);
    }

    [Theory]
    [InlineData(-1000, -700, 8)]
    [InlineData(3000, 3300, 1612)]
    public void Calculate_ClampsPriorCenterPreferenceInsideHorizontalMargins(
        int priorLeft,
        int priorRight,
        int expectedLeft)
    {
        var workArea = new PixelRect(0, 0, 1920, 1040);
        var priorNativeBounds = new PixelRect(priorLeft, 1000, priorRight, 1040);

        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(workArea, 96, priorNativeBounds));

        Assert.Equal(expectedLeft, AssertAvailable(result).Left);
    }

    [Fact]
    public void Calculate_AllowsAnExactFitIncludingMargins()
    {
        var workArea = new PixelRect(10, 20, 326, 68);

        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(workArea, 96));

        Assert.Equal(
            new PixelRect(18, 20, 318, 60),
            AssertAvailable(result));
    }

    [Fact]
    public void Calculate_RejectsAWorkAreaOnePixelTooNarrow()
    {
        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(new PixelRect(0, 0, 315, 56), 96));

        AssertUnavailable(result, FloatingPlacementUnavailableReason.InsufficientWidth);
    }

    [Fact]
    public void Calculate_RejectsAWorkAreaOnePixelTooShort()
    {
        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(new PixelRect(0, 0, 316, 47), 96));

        AssertUnavailable(result, FloatingPlacementUnavailableReason.InsufficientHeight);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(100, 0, 100, 100)]
    [InlineData(0, 100, 100, 100)]
    [InlineData(int.MinValue, 0, int.MaxValue, 100)]
    public void Calculate_RejectsInvalidOrUnrepresentableWorkAreas(
        int left,
        int top,
        int right,
        int bottom)
    {
        var workArea = new PixelRect(left, top, right, bottom);

        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(workArea, 96));

        AssertUnavailable(result, FloatingPlacementUnavailableReason.InvalidWorkArea);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(95u)]
    [InlineData(241u)]
    [InlineData(uint.MaxValue)]
    public void Calculate_RejectsUnsupportedDpi(uint dpi)
    {
        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(new PixelRect(0, 0, 1920, 1040), dpi));

        AssertUnavailable(result, FloatingPlacementUnavailableReason.UnsupportedDpi);
    }

    [Theory]
    [InlineData(96u, 300, 40, 8)]
    [InlineData(240u, 750, 100, 20)]
    public void Calculate_AcceptsSupportedDpiBoundaries(
        uint dpi,
        int width,
        int height,
        int margin)
    {
        var workArea = new PixelRect(0, 0, width + (2 * margin), height + margin);

        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(workArea, dpi));

        Assert.Equal(
            new PixelRect(margin, 0, margin + width, height),
            AssertAvailable(result));
    }

    [Fact]
    public void Calculate_RejectsAnInvalidPriorNativeBoundsPreference()
    {
        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(
                new PixelRect(0, 0, 1920, 1040),
                96,
                new PixelRect(10, 10, 10, 50)));

        AssertUnavailable(
            result,
            FloatingPlacementUnavailableReason.InvalidPriorNativeBounds);
    }

    [Fact]
    public void Calculate_DoesNotOverflowNearMaximumPhysicalCoordinates()
    {
        var workArea = new PixelRect(
            int.MaxValue - 316,
            int.MaxValue - 48,
            int.MaxValue,
            int.MaxValue);

        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(workArea, 96));

        Assert.Equal(
            new PixelRect(
                int.MaxValue - 308,
                int.MaxValue - 48,
                int.MaxValue - 8,
                int.MaxValue - 8),
            AssertAvailable(result));
    }

    [Fact]
    public void Calculate_DoesNotOverflowNearMinimumPhysicalCoordinates()
    {
        var workArea = new PixelRect(
            int.MinValue,
            int.MinValue,
            int.MinValue + 316,
            int.MinValue + 48);

        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(workArea, 96));

        Assert.Equal(
            new PixelRect(
                int.MinValue + 8,
                int.MinValue,
                int.MinValue + 308,
                int.MinValue + 40),
            AssertAvailable(result));
    }

    [Fact]
    public void Calculate_ClampsAnExtremeValidPriorCenterWithoutOverflow()
    {
        var priorNativeBounds = new PixelRect(int.MinValue, -10, -1, 10);

        FloatingPlacementResult result = FloatingPlacementCalculator.Calculate(
            new(new PixelRect(0, 0, 1920, 1040), 96, priorNativeBounds));

        Assert.Equal(8, AssertAvailable(result).Left);
    }

    private static PixelRect AssertAvailable(FloatingPlacementResult result)
    {
        Assert.True(result.IsAvailable);
        Assert.Equal(FloatingPlacementUnavailableReason.None, result.UnavailableReason);
        return Assert.IsType<PixelRect>(result.Bounds);
    }

    private static void AssertUnavailable(
        FloatingPlacementResult result,
        FloatingPlacementUnavailableReason expectedReason)
    {
        Assert.False(result.IsAvailable);
        Assert.Null(result.Bounds);
        Assert.Equal(expectedReason, result.UnavailableReason);
    }
}
