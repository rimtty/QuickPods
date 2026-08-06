using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Placement;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class TaskbarPlacementTests
{
    [Fact]
    public void CompleteCenteredTaskbarChoosesStandardPlacementWithoutOverlap()
    {
        TaskbarLayoutObservation observation = Observation(
            taskbar: new PixelRect(0, 0, 3840, 48),
            start: new PixelRect(1568, 0, 1613, 48),
            widgets: new PixelRect(6, 0, 158, 48),
            obstacles: [new PixelRect(500, 0, 760, 48)]);

        TaskbarPlacementResult result = SafeRegionPlanner.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(TaskbarStripMode.Standard, result.Mode);
        Assert.NotNull(result.Bounds);
        Assert.Equal(300, result.Bounds.Value.Width);
        Assert.Equal(0, result.Bounds.Value.IntersectionArea(observation.Obstacles[0]));
        Assert.True(SafeRegionPlanner.IsExistingPlacementSafe(
            observation,
            TaskbarPlacementOptions.Default,
            result.Bounds.Value));
    }

    [Fact]
    public void LeftAlignedStartIsKnownUnsupportedConfiguration()
    {
        TaskbarLayoutObservation observation = Observation(
            taskbar: new PixelRect(0, 0, 1920, 48),
            start: new PixelRect(0, 0, 45, 48));

        TaskbarPlacementResult result = SafeRegionPlanner.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.UnsupportedConfiguration, result.Decision);
        Assert.Equal(PlacementReason.UnsupportedAlignment, result.Reason);
        Assert.Null(result.Bounds);
        Assert.False(SafeRegionPlanner.IsExistingPlacementSafe(
            observation,
            TaskbarPlacementOptions.Default,
            new PixelRect(100, 4, 400, 44)));
    }

    [Fact]
    public void CenterAlignedTaskbarWithoutCompactGapIsVerifiedNoFit()
    {
        TaskbarLayoutObservation observation = Observation(
            taskbar: new PixelRect(0, 0, 1920, 48),
            start: new PixelRect(1000, 0, 1045, 48),
            obstacles: [new PixelRect(0, 0, 850, 48)]);

        TaskbarPlacementResult result = SafeRegionPlanner.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.VerifiedNoFit, result.Decision);
        Assert.Equal(PlacementReason.InsufficientWidth, result.Reason);
        Assert.Null(result.Bounds);
    }

    [Fact]
    public void IncompleteOrContradictoryEvidenceFailsClosed()
    {
        TaskbarLayoutObservation incomplete = Observation(
            taskbar: new PixelRect(0, 0, 1920, 48),
            start: new PixelRect(900, 0, 945, 48)) with
        {
            IsComplete = false,
        };
        TaskbarLayoutObservation contradictory = Observation(
            taskbar: new PixelRect(0, 0, 1920, 48),
            start: new PixelRect(900, 0, 945, 48),
            widgets: new PixelRect(850, 0, 930, 48));

        Assert.Equal(
            PlacementDecision.TransientUnknown,
            SafeRegionPlanner.Calculate(incomplete, TaskbarPlacementOptions.Default).Decision);
        Assert.Equal(
            PlacementReason.ContradictoryLandmarks,
            SafeRegionPlanner.Calculate(contradictory, TaskbarPlacementOptions.Default).Reason);
    }

    [Theory]
    [InlineData(96, 300)]
    [InlineData(120, 375)]
    [InlineData(144, 450)]
    [InlineData(192, 600)]
    public void DpiConversionUsesPhysicalPixels(uint dpi, int expectedWidth)
    {
        TaskbarLayoutObservation observation = Observation(
            taskbar: new PixelRect(-3840, 0, 0, 120),
            start: new PixelRect(-1000, 0, -900, 120),
            dpi: dpi);

        TaskbarPlacementResult result = SafeRegionPlanner.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(expectedWidth, result.Bounds?.Width);
        Assert.True(result.Bounds?.Left < 0);
    }

    [Fact]
    public void ObstacleFragmentationCanProduceCompactPlacement()
    {
        TaskbarLayoutObservation observation = Observation(
            taskbar: new PixelRect(0, 0, 1920, 48),
            start: new PixelRect(1000, 0, 1045, 48),
            obstacles:
            [
                new PixelRect(0, 0, 300, 48),
                new PixelRect(550, 0, 1000, 48),
            ]);

        TaskbarPlacementResult result = SafeRegionPlanner.Calculate(
            observation,
            TaskbarPlacementOptions.Default);

        Assert.Equal(PlacementDecision.Place, result.Decision);
        Assert.Equal(TaskbarStripMode.Compact, result.Mode);
        Assert.InRange(result.Bounds?.Width ?? 0, 190, 299);
    }

    private static TaskbarLayoutObservation Observation(
        PixelRect taskbar,
        PixelRect start,
        PixelRect? widgets = null,
        IReadOnlyList<PixelRect>? obstacles = null,
        uint dpi = 96) =>
        new(
            taskbar,
            dpi,
            TaskbarOrientation.Horizontal,
            start,
            widgets,
            obstacles ?? [],
            IsComplete: true);
}
