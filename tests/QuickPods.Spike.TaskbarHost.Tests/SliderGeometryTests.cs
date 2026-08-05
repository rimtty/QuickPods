using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class SliderGeometryTests
{
    [Theory]
    [InlineData(96)]
    [InlineData(192)]
    public void DrawingAndInput_UseSameTrackAtSupportedDpi(uint dpi)
    {
        Assert.True(DipPixelConverter.TryDipLengthToPixels(300, dpi, out int width));
        Assert.True(DipPixelConverter.TryDipLengthToPixels(40, dpi, out int height));
        Assert.True(SliderGeometry.TryCreate(width, height, out SliderLayout layout));

        Assert.Equal(0d, SliderGeometry.FractionFromPointerX(layout, layout.TrackLeft));
        Assert.Equal(1d, SliderGeometry.FractionFromPointerX(layout, layout.TrackRight));

        int midpoint = layout.TrackLeft + ((layout.TrackRight - layout.TrackLeft) / 2);
        Assert.InRange(SliderGeometry.FractionFromPointerX(layout, midpoint), 0.49d, 0.51d);
        Assert.True(SliderGeometry.ContainsPointer(layout, midpoint, layout.CenterY));
        Assert.False(SliderGeometry.ContainsPointer(layout, layout.TrackLeft - 1, layout.CenterY));
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(1, 1)]
    public void TryCreate_InvalidSurface_ReturnsFalse(int width, int height)
    {
        Assert.False(SliderGeometry.TryCreate(width, height, out _));
    }
}
