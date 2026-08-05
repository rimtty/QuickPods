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
    public void ExistingPlacementOutsideCandidateLaneIsUnsafe()
    {
        TaskbarLayoutObservation observation = CreateObservation();
        var outsideCandidateLane = new PixelRect(157, 4, 347, 44);

        bool isSafe = SafeRegionCalculator.IsExistingPlacementSafe(
            observation,
            TaskbarPlacementOptions.Default,
            outsideCandidateLane);

        Assert.False(isSafe);
    }

    [Theory]
    [InlineData(200, 3, 390, 43)]
    [InlineData(200, 4, 390, 43)]
    public void ExistingPlacementWithWrongVerticalBandOrHeightIsUnsafe(
        int left,
        int top,
        int right,
        int bottom)
    {
        var existingBounds = new PixelRect(left, top, right, bottom);

        bool isSafe = SafeRegionCalculator.IsExistingPlacementSafe(
            CreateObservation(),
            TaskbarPlacementOptions.Default,
            existingBounds);

        Assert.False(isSafe);
    }

    [Theory]
    [InlineData(200, 389)]
    [InlineData(200, 501)]
    public void ExistingPlacementOutsideCompactToStandardWidthRangeIsUnsafe(
        int left,
        int right)
    {
        var existingBounds = new PixelRect(left, 4, right, 44);

        bool isSafe = SafeRegionCalculator.IsExistingPlacementSafe(
            CreateObservation(),
            TaskbarPlacementOptions.Default,
            existingBounds);

        Assert.False(isSafe);
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
    public void ExistingPlacementIntersectingRawObstacleIsUnsafeEvenWithZeroMargin()
    {
        var obstacle = new PixelRect(400, 0, 500, 48);
        TaskbarLayoutObservation observation = CreateObservation(obstacles: [obstacle]);
        var overlappingBounds = new PixelRect(350, 4, 540, 44);
        TaskbarPlacementOptions options = TaskbarPlacementOptions.Default with { MarginDip = 0d };
        TaskbarPlacementResult preferred = SafeRegionCalculator.Calculate(observation, options);
        Assert.True(overlappingBounds.IntersectionArea(obstacle) > 0);
        Assert.Equal(PlacementDecision.Place, preferred.Decision);
        Assert.NotEqual(preferred.Bounds, overlappingBounds);

        bool isSafe = SafeRegionCalculator.IsExistingPlacementSafe(
            observation,
            options,
            overlappingBounds);

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
    [InlineData(120u, 2400, 60, 188, 1125, 375, 50)]
    [InlineData(144u, 2880, 72, 225, 1350, 450, 60)]
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
    public void ObstaclesAreClippedToTheTaskbarBeforeExpansion()
    {
        var partiallyOutsideObstacle = new PixelRect(-1000, -10, 300, 60);
        TaskbarLayoutObservation observation = CreateObservation(obstacles: [partiallyOutsideObstacle]);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        var expected = new PixelRect(450, 4, 750, 44);
        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(expected, result.Bounds);
        Assert.Equal(0, expected.IntersectionArea(partiallyOutsideObstacle));
    }

    [Fact]
    public void ObstacleOutsideTheVerticalPlacementBandDoesNotConsumeHorizontalSpace()
    {
        var outsideBand = new PixelRect(300, -100, 700, 0);
        TaskbarLayoutObservation observation = CreateObservation(obstacles: [outsideBand]);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(new PixelRect(375, 4, 675, 44), result.Bounds);
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
    public void EqualMaximumGapsUseTheLeftmostGapDeterministically()
    {
        var taskbar = new PixelRect(0, 0, 800, 48);
        var start = new PixelRect(718, 0, 766, 48);
        var obstacle = new PixelRect(208, 0, 510, 48);
        TaskbarLayoutObservation observation = CreateObservation(
            taskbar,
            start,
            includeWidgets: false,
            obstacles: [obstacle]);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(TaskbarStripMode.Compact, result.Mode);
        Assert.Equal(new PixelRect(8, 4, 200, 44), result.Bounds);
    }

    [Fact]
    public void PlacementDimensionsAndMarginsAreInputValues()
    {
        var options = new TaskbarPlacementOptions(
            StandardWidthDip: 240d,
            CompactMinimumWidthDip: 160d,
            HeightDip: 32d,
            MarginDip: 12d);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(CreateObservation(), options);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(240, result.Bounds!.Value.Width);
        Assert.Equal(32, result.Bounds.Value.Height);
        Assert.Equal(8, result.Bounds.Value.Top);
    }

    [Fact]
    public void StripTallerThanVerifiedTaskbarIsVerifiedNoFit()
    {
        TaskbarPlacementOptions options = TaskbarPlacementOptions.Default with { HeightDip = 64d };

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(CreateObservation(), options);

        AssertNoFit(result, PlacementReason.InsufficientHeight);
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
    [InlineData(2)]
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
    public void GeometryClaimingHorizontalButShapedVerticalIsTransientUnknown()
    {
        var taskbar = new PixelRect(0, 0, 48, 1000);
        var widgets = new PixelRect(0, 0, 48, 100);
        var start = new PixelRect(0, 400, 48, 448);
        TaskbarLayoutObservation observation = CreateObservation(taskbar, start, widgets);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertUnknown(result, PlacementReason.InvalidGeometry);
    }

    [Fact]
    public void LandmarkOutsideTaskbarIsTransientUnknown()
    {
        TaskbarLayoutObservation observation = CreateObservation(
            start: new PixelRect(900, -1, 948, 48));

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertUnknown(result, PlacementReason.InvalidGeometry);
    }

    [Fact]
    public void WidgetsToTheRightOfStartIsContradictoryAndTransientUnknown()
    {
        TaskbarLayoutObservation observation = CreateObservation(
            start: new PixelRect(500, 0, 548, 48),
            widgets: new PixelRect(490, 0, 510, 48));

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertUnknown(result, PlacementReason.ContradictoryLandmarks);
    }

    [Fact]
    public void NullObstacleCollectionIsIncompleteAndTransientUnknown()
    {
        TaskbarLayoutObservation observation = CreateObservation(includeObstacleCollection: false);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertUnknown(result, PlacementReason.InvalidGeometry);
    }

    [Fact]
    public void InvalidObstacleIsTransientUnknown()
    {
        TaskbarLayoutObservation observation = CreateObservation(obstacles: [default]);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertUnknown(result, PlacementReason.InvalidGeometry);
    }

    [Fact]
    public void InvalidPlacementOptionsAreTransientUnknown()
    {
        TaskbarPlacementOptions[] invalidOptions =
        [
            TaskbarPlacementOptions.Default with { StandardWidthDip = double.NaN },
            TaskbarPlacementOptions.Default with { CompactMinimumWidthDip = 0d },
            TaskbarPlacementOptions.Default with { HeightDip = -1d },
            TaskbarPlacementOptions.Default with { MarginDip = -1d },
            new TaskbarPlacementOptions(180d, 190d, 40d, 8d),
        ];

        foreach (TaskbarPlacementOptions options in invalidOptions)
        {
            TaskbarPlacementResult result = SafeRegionCalculator.Calculate(CreateObservation(), options);
            AssertUnknown(result, PlacementReason.InvalidGeometry);
        }
    }

    [Fact]
    public void MetricConversionOverflowIsTransientUnknown()
    {
        TaskbarPlacementOptions options = TaskbarPlacementOptions.Default with { StandardWidthDip = double.MaxValue };

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(CreateObservation(), options);

        AssertUnknown(result, PlacementReason.ConversionFailed);
    }

    [Fact]
    public void ObstacleExpansionOverflowIsTransientUnknown()
    {
        var taskbar = new PixelRect(int.MinValue, 0, int.MinValue + 1000, 48);
        var widgets = new PixelRect(int.MinValue, 0, int.MinValue + 100, 48);
        var start = new PixelRect(int.MinValue + 900, 0, int.MinValue + 948, 48);
        var obstacle = new PixelRect(int.MinValue, 0, int.MinValue + 200, 48);
        TaskbarLayoutObservation observation = CreateObservation(taskbar, start, widgets, [obstacle]);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertUnknown(result, PlacementReason.ConversionFailed);
    }

    [Fact]
    public void CandidateMarginOverflowIsTransientUnknownRatherThanNoFit()
    {
        var taskbar = new PixelRect(int.MaxValue - 100, 0, int.MaxValue, 48);
        var widgets = new PixelRect(int.MaxValue - 100, 0, int.MaxValue - 4, 48);
        var start = new PixelRect(int.MaxValue - 3, 0, int.MaxValue, 48);
        TaskbarLayoutObservation observation = CreateObservation(taskbar, start, widgets);

        TaskbarPlacementResult result = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        AssertUnknown(result, PlacementReason.ConversionFailed);
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
