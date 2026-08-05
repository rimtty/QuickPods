using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class SafeRegionCalculatorTests
{
    [Fact]
    public void CompleteCentralLayoutPlacesAStandardStripBetweenWidgetsAndStart()
    {
        TaskbarLayoutObservation observation = CreateObservation();

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(TaskbarStripMode.Standard, result.Mode);
        Assert.Equal(new PixelRect(375, 4, 675, 44), result.Bounds);
        Assert.Equal(PlacementReason.None, result.Reason);
    }

    [Theory]
    [InlineData(200, 390)]
    [InlineData(200, 500)]
    public void ExistingCompactOrStandardPlacementRemainsSafeWhenPreferredBoundsDrift(
        int existingLeft,
        int existingRight)
    {
        TaskbarLayoutObservation observation = CreateObservation();
        var existingBounds = new PixelRect(existingLeft, 4, existingRight, 44);
        TaskbarPlacementResult preferred = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        bool isSafe = SafeRegionCalculator.IsExistingPlacementSafe(
            observation,
            TaskbarPlacementOptions.Default,
            existingBounds);

        Assert.NotEqual(preferred.Bounds, existingBounds);
        Assert.True(isSafe);
    }

    [Fact]
    public void ExistingPlacementWithinRawClearanceButInsideExpandedObstacleMarginIsUnsafe()
    {
        var obstacle = new PixelRect(400, 0, 500, 48);
        TaskbarLayoutObservation observation = CreateObservation(obstacles: [obstacle]);
        var insideExpandedMargin = new PixelRect(203, 4, 393, 44);
        TaskbarPlacementResult preferred = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);
        Assert.Equal(0, insideExpandedMargin.IntersectionArea(obstacle));
        Assert.Equal(PlacementDecision.Place, preferred.Decision);
        Assert.NotEqual(preferred.Bounds, insideExpandedMargin);

        bool isSafe = SafeRegionCalculator.IsExistingPlacementSafe(
            observation,
            TaskbarPlacementOptions.Default,
            insideExpandedMargin);

        Assert.False(isSafe);
    }

    [Fact]
    public void ExistingPlacementFailsClosedForIncompleteOrInvalidInputs()
    {
        var validBounds = new PixelRect(200, 4, 390, 44);
        TaskbarLayoutObservation incomplete = CreateObservation(isComplete: false);
        TaskbarLayoutObservation invalidObstacle = CreateObservation(obstacles: [default]);
        TaskbarLayoutObservation conversionOverflow = CreateObservation(dpi: uint.MaxValue);
        TaskbarPlacementOptions invalidOptions =
            TaskbarPlacementOptions.Default with { MarginDip = double.NaN };

        Assert.False(SafeRegionCalculator.IsExistingPlacementSafe(
            null,
            TaskbarPlacementOptions.Default,
            validBounds));
        Assert.False(SafeRegionCalculator.IsExistingPlacementSafe(
            incomplete,
            TaskbarPlacementOptions.Default,
            validBounds));
        Assert.False(SafeRegionCalculator.IsExistingPlacementSafe(
            invalidObstacle,
            TaskbarPlacementOptions.Default,
            validBounds));
        Assert.False(SafeRegionCalculator.IsExistingPlacementSafe(
            conversionOverflow,
            TaskbarPlacementOptions.Default,
            validBounds));
        Assert.False(SafeRegionCalculator.IsExistingPlacementSafe(
            CreateObservation(),
            invalidOptions,
            validBounds));
        Assert.False(SafeRegionCalculator.IsExistingPlacementSafe(
            CreateObservation(),
            TaskbarPlacementOptions.Default,
            default));
    }

    [Fact]
    public void WidgetsAreOptionalAndTaskbarLeftEdgeBecomesTheLeadingLandmark()
    {
        TaskbarLayoutObservation observation = CreateObservation(includeWidgets: false);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(new PixelRect(300, 4, 600, 44), result.Bounds);
    }

    [Theory]
    [InlineData(96u, 1920, 48, 150, 900, 300, 40)]
    [InlineData(192u, 3840, 96, 300, 1800, 600, 80)]
    public void PlacementUsesPhysicalPixelsAtSupportedDpiScales(
        uint dpi,
        int taskbarWidth,
        int taskbarHeight,
        int widgetsRight,
        int startLeft,
        int expectedWidth,
        int expectedHeight)
    {
        var taskbar = new PixelRect(0, 0, taskbarWidth, taskbarHeight);
        var widgets = new PixelRect(0, 0, widgetsRight, taskbarHeight);
        var start = new PixelRect(startLeft, 0, startLeft + taskbarHeight, taskbarHeight);
        TaskbarLayoutObservation observation = CreateObservation(
            taskbar: taskbar,
            start: start,
            widgets: widgets,
            dpi: dpi);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.NotNull(result.Bounds);
        Assert.Equal(expectedWidth, result.Bounds.Value.Width);
        Assert.Equal(expectedHeight, result.Bounds.Value.Height);
        Assert.Equal(0, result.Bounds.Value.IntersectionArea(start));
        Assert.Equal(0, result.Bounds.Value.IntersectionArea(widgets));
    }

    [Fact]
    public void NegativeVirtualDesktopCoordinatesArePlacedWithoutClampingToZero()
    {
        var taskbar = new PixelRect(-1920, -48, 0, 0);
        var start = new PixelRect(-1020, -48, -972, 0);
        var widgets = new PixelRect(-1920, -48, -1770, 0);
        TaskbarLayoutObservation observation = CreateObservation(taskbar, start, widgets);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(new PixelRect(-1545, -44, -1245, -4), result.Bounds);
    }

    [Fact]
    public void ObstaclesAreExpandedByTheConfiguredMarginBeforeSubtraction()
    {
        var obstacle = new PixelRect(400, 0, 500, 48);
        TaskbarLayoutObservation observation = CreateObservation(obstacles: [obstacle]);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        var expected = new PixelRect(550, 4, 850, 44);
        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(expected, result.Bounds);
        Assert.True(expected.Left >= obstacle.Right + 8);
        Assert.Equal(0, expected.IntersectionArea(obstacle));
    }

    [Fact]
    public void GapBelowStandardButAtCompactMinimumUsesAllAvailableCompactWidth()
    {
        var taskbar = new PixelRect(0, 0, 600, 48);
        var widgets = new PixelRect(0, 0, 100, 48);
        var start = new PixelRect(306, 0, 354, 48);
        TaskbarLayoutObservation observation = CreateObservation(taskbar, start, widgets);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(TaskbarStripMode.Compact, result.Mode);
        Assert.Equal(new PixelRect(108, 4, 298, 44), result.Bounds);
    }

    [Fact]
    public void GapOnePixelBelowCompactMinimumIsVerifiedNoFit()
    {
        var taskbar = new PixelRect(0, 0, 600, 48);
        var widgets = new PixelRect(0, 0, 100, 48);
        var start = new PixelRect(305, 0, 353, 48);
        TaskbarLayoutObservation observation = CreateObservation(taskbar, start, widgets);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertNoFit(result, PlacementReason.InsufficientWidth);
    }

    [Fact]
    public void FullyBlockedCandidateLaneIsVerifiedNoFit()
    {
        TaskbarLayoutObservation observation = CreateObservation(
            obstacles: [new PixelRect(100, 0, 950, 48)]);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertNoFit(result, PlacementReason.InsufficientWidth);
    }

    [Fact]
    public void MissingStartLandmarkIsTransientUnknown()
    {
        TaskbarLayoutObservation observation = CreateObservation(includeStart: false);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertUnknown(result, PlacementReason.InvalidGeometry);
    }

    [Fact]
    public void IncompleteEnumerationIsTransientUnknown()
    {
        TaskbarLayoutObservation observation = CreateObservation(isComplete: false);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertUnknown(result, PlacementReason.IncompleteObservation);
    }

    [Theory]
    [InlineData(0)]
    public void NonHorizontalTaskbarsAreTransientUnknown(int orientationValue)
    {
        var orientation = (TaskbarOrientation)orientationValue;
        TaskbarLayoutObservation observation = CreateObservation(orientation: orientation);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertUnknown(result, PlacementReason.UnsupportedOrientation);
    }

    [Fact]
    public void EveryPlacedResultHasZeroIntersectionWithEveryObservedObstacle()
    {
        PixelRect[] obstacles =
        [
            new(170, 0, 220, 48),
            new(500, 0, 550, 48),
            new(1000, 0, 1100, 48),
        ];
        TaskbarLayoutObservation observation = CreateObservation(obstacles: obstacles);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.All(obstacles, obstacle => Assert.Equal(0, result.Bounds!.Value.IntersectionArea(obstacle)));
        Assert.Equal(0, result.Bounds!.Value.IntersectionArea(observation.StartButtonBounds!.Value));
        Assert.Equal(0, result.Bounds!.Value.IntersectionArea(observation.WidgetsButtonBounds!.Value));
    }

    private static TaskbarLayoutObservation CreateObservation(
        PixelRect? taskbar = null,
        PixelRect? start = null,
        PixelRect? widgets = default,
        IReadOnlyList<PixelRect>? obstacles = null,
        uint dpi = 96,
        TaskbarOrientation orientation = TaskbarOrientation.Horizontal,
        bool isComplete = true,
        bool includeStart = true,
        bool includeWidgets = true,
        bool includeObstacleCollection = true)
    {
        PixelRect actualTaskbar = taskbar ?? new PixelRect(0, 0, 1920, 48);
        PixelRect? actualStart = includeStart
            ? start ?? new PixelRect(900, 0, 948, 48)
            : null;
        PixelRect? actualWidgets = includeWidgets
            ? widgets ?? new PixelRect(0, 0, 150, 48)
            : null;
        IReadOnlyList<PixelRect> actualObstacles = includeObstacleCollection
            ? obstacles ?? []
            : null!;

        return new TaskbarLayoutObservation(
            actualTaskbar,
            dpi,
            orientation,
            actualStart,
            actualWidgets,
            actualObstacles,
            isComplete);
    }

    private static void AssertNoFit(TaskbarPlacementResult result, PlacementReason reason)
    {
        Assert.Equal(PlacementDecision.VerifiedNoFit, result.Decision);
        Assert.Null(result.Bounds);
        Assert.Null(result.Mode);
        Assert.Equal(reason, result.Reason);
    }

    private static void AssertUnknown(TaskbarPlacementResult result, PlacementReason reason)
    {
        Assert.Equal(PlacementDecision.TransientUnknown, result.Decision);
        Assert.Null(result.Bounds);
        Assert.Null(result.Mode);
        Assert.Equal(reason, result.Reason);
    }
}
