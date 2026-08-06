using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Placement;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class FloatingPlacementTests
{
    [Fact]
    public void SupportedWorkAreasProduceContainedBottomPlacements()
    {
        FloatingPlacementResult standard = FloatingPlacementCalculator.Calculate(
            new(new PixelRect(0, 0, 3840, 2160), 96));
        FloatingPlacementResult negative = FloatingPlacementCalculator.Calculate(
            new(new PixelRect(-1920, -1080, 0, 0), 96));

        Assert.Equal(new PixelRect(1770, 2112, 2070, 2152), standard.Bounds);
        Assert.Equal(new PixelRect(-1110, -48, -810, -8), negative.Bounds);
        Assert.True(standard.IsAvailable);
        Assert.True(negative.IsAvailable);
    }

    [Fact]
    public void UnsafeInputsReturnExplicitUnavailableReasonsWithoutBounds()
    {
        FloatingPlacementResult invalid = FloatingPlacementCalculator.Calculate(
            new(default, 96));
        FloatingPlacementResult unsupportedDpi = FloatingPlacementCalculator.Calculate(
            new(new PixelRect(0, 0, 1920, 1040), 0));
        FloatingPlacementResult narrow = FloatingPlacementCalculator.Calculate(
            new(new PixelRect(0, 0, 315, 56), 96));

        Assert.Equal(FloatingPlacementUnavailableReason.InvalidWorkArea, invalid.UnavailableReason);
        Assert.Equal(FloatingPlacementUnavailableReason.UnsupportedDpi, unsupportedDpi.UnavailableReason);
        Assert.Equal(FloatingPlacementUnavailableReason.InsufficientWidth, narrow.UnavailableReason);
        Assert.Null(invalid.Bounds);
        Assert.Null(unsupportedDpi.Bounds);
        Assert.Null(narrow.Bounds);
    }
}
