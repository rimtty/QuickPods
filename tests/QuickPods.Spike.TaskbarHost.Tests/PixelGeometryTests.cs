using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class PixelGeometryTests
{
    [Fact]
    public void PixelIntervalUsesHalfOpenCoordinates()
    {
        var left = new PixelInterval(-20, 0);
        var touchingRight = new PixelInterval(0, 20);

        Assert.True(left.IsValid);
        Assert.Equal(20, left.Length);
        Assert.False(left.Intersects(touchingRight));
    }

    [Fact]
    public void PixelIntervalRejectsAnUnrepresentableSpan()
    {
        var interval = new PixelInterval(int.MinValue, int.MaxValue);

        Assert.False(interval.IsValid);
        Assert.Equal(0, interval.Length);
    }

    [Fact]
    public void PixelIntervalClipsToPhysicalBounds()
    {
        var interval = new PixelInterval(-100, 100);

        bool clipped = interval.TryClipTo(new PixelInterval(-20, 40), out PixelInterval result);

        Assert.True(clipped);
        Assert.Equal(new PixelInterval(-20, 40), result);
    }

    [Fact]
    public void PixelRectCreatesAValidNegativeVirtualDesktopRectangle()
    {
        bool created = PixelRect.TryCreateFromPositionAndSize(-1920, -48, 1920, 48, out PixelRect rectangle);

        Assert.True(created);
        Assert.Equal(-1920, rectangle.X);
        Assert.Equal(-48, rectangle.Y);
        Assert.Equal(1920, rectangle.Width);
        Assert.Equal(48, rectangle.Height);
        Assert.Equal(new PixelInterval(-1920, 0), rectangle.HorizontalInterval);
    }

    [Fact]
    public void PixelRectCreateFailsClosedWhenTrailingEdgeOverflows()
    {
        bool created = PixelRect.TryCreateFromPositionAndSize(int.MaxValue - 5, 0, 10, 10, out PixelRect rectangle);

        Assert.False(created);
        Assert.False(rectangle.IsValid);
    }

    [Fact]
    public void PixelRectIntersectionAreaIsZeroForTouchingEdges()
    {
        var left = new PixelRect(0, 0, 10, 10);
        var touchingRight = new PixelRect(10, 0, 20, 10);

        Assert.False(left.Intersects(touchingRight));
        Assert.Equal(0, left.IntersectionArea(touchingRight));
    }

    [Fact]
    public void PixelRectIntersectionUsesPhysicalPixelArea()
    {
        var first = new PixelRect(-10, -10, 10, 10);
        var second = new PixelRect(0, -5, 20, 20);

        Assert.True(first.TryIntersect(second, out PixelRect intersection));
        Assert.Equal(new PixelRect(0, -5, 10, 10), intersection);
        Assert.Equal(150, first.IntersectionArea(second));
    }

    [Fact]
    public void ExpandAddsPaddingToBothSides()
    {
        bool expanded = IntervalGeometry.TryExpand(new PixelInterval(100, 200), 8, out PixelInterval result);

        Assert.True(expanded);
        Assert.Equal(new PixelInterval(92, 208), result);
    }

    [Theory]
    [InlineData(int.MinValue, 10)]
    [InlineData(0, int.MaxValue)]
    public void ExpandFailsClosedOnCoordinateOverflow(int start, int end)
    {
        bool expanded = IntervalGeometry.TryExpand(new PixelInterval(start, end), 8, out _);

        Assert.False(expanded);
    }

    [Fact]
    public void MergeSortsAndCoalescesOverlappingAndAdjacentIntervals()
    {
        PixelInterval[] intervals =
        [
            new(40, 50),
            new(0, 10),
            new(8, 20),
            new(20, 30),
            new(60, 70),
        ];

        bool merged = IntervalGeometry.TryMerge(intervals, out IReadOnlyList<PixelInterval> result);

        Assert.True(merged);
        Assert.Equal(
            [new PixelInterval(0, 30), new PixelInterval(40, 50), new PixelInterval(60, 70)],
            result);
    }

    [Fact]
    public void MergeFailsClosedForAnInvalidInterval()
    {
        bool merged = IntervalGeometry.TryMerge(
            [new PixelInterval(0, 10), default],
            out IReadOnlyList<PixelInterval> result);

        Assert.False(merged);
        Assert.Empty(result);
    }

    [Fact]
    public void MergeFailsClosedWhenTheCombinedSpanIsUnrepresentable()
    {
        PixelInterval[] intervals =
        [
            new(int.MinValue, -1),
            new(-1, int.MaxValue - 1),
        ];

        bool merged = IntervalGeometry.TryMerge(intervals, out IReadOnlyList<PixelInterval> result);

        Assert.False(merged);
        Assert.Empty(result);
    }

    [Fact]
    public void SubtractClipsMergesAndReturnsAllRemainingGaps()
    {
        PixelInterval[] obstacles =
        [
            new(-20, 10),
            new(20, 30),
            new(25, 50),
            new(90, 120),
        ];

        bool subtracted = IntervalGeometry.TrySubtract(
            new PixelInterval(0, 100),
            obstacles,
            out IReadOnlyList<PixelInterval> gaps);

        Assert.True(subtracted);
        Assert.Equal([new PixelInterval(10, 20), new PixelInterval(50, 90)], gaps);
    }

    [Fact]
    public void SubtractReturnsNoGapWhenAnObstacleCoversTheSource()
    {
        bool subtracted = IntervalGeometry.TrySubtract(
            new PixelInterval(0, 100),
            [new PixelInterval(-10, 110)],
            out IReadOnlyList<PixelInterval> gaps);

        Assert.True(subtracted);
        Assert.Empty(gaps);
    }

    [Fact]
    public void MaximumGapPrefersTheLeftmostIntervalOnAnEqualWidthTie()
    {
        PixelInterval[] gaps = [new(500, 700), new(10, 210), new(250, 300)];

        bool found = IntervalGeometry.TryFindMaximumGap(gaps, out PixelInterval maximum);

        Assert.True(found);
        Assert.Equal(new PixelInterval(10, 210), maximum);
    }
}
