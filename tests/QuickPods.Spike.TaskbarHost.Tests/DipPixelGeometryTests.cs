using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class DipPixelGeometryTests
{
    [Theory]
    [InlineData(96u, 300)]
    [InlineData(192u, 600)]
    public void DipLengthUsesNinetySixDipPerLogicalInch(uint dpi, int expectedPixels)
    {
        bool converted = DipPixelConverter.TryDipLengthToPixels(300d, dpi, out int actualPixels);

        Assert.True(converted);
        Assert.Equal(expectedPixels, actualPixels);
    }

    [Theory]
    [InlineData(144u, -12)]
    public void DipCoordinatePreservesNegativeVirtualDesktopCoordinates(uint dpi, int expectedPixels)
    {
        bool converted = DipPixelConverter.TryDipToPixel(-8d, dpi, out int actualPixels);

        Assert.True(converted);
        Assert.Equal(expectedPixels, actualPixels);
    }

    [Fact]
    public void DipCoordinateRoundsMidpointsAwayFromZero()
    {
        Assert.True(DipPixelConverter.TryDipToPixel(0.5d, 96, out int positive));
        Assert.True(DipPixelConverter.TryDipToPixel(-0.5d, 96, out int negative));

        Assert.Equal(1, positive);
        Assert.Equal(-1, negative);
    }

    [Fact]
    public void DipRectangleRoundsOutwardForSafety()
    {
        var dipRectangle = new DipRect(-1d, -1d, 1d, 1d);

        bool converted = DipPixelConverter.TryDipRectToPixels(dipRectangle, 120, out PixelRect pixels);

        Assert.True(converted);
        Assert.Equal(new PixelRect(-2, -2, 2, 2), pixels);
    }

    [Fact]
    public void PixelRectangleRoundTripsAtIntegralScale()
    {
        var pixels = new PixelRect(-300, -60, 300, 60);

        bool converted = DipPixelConverter.TryPixelRectToDips(pixels, 192, out DipRect dips);

        Assert.True(converted);
        Assert.Equal(new DipRect(-150d, -30d, 150d, 30d), dips);
    }

    [Theory]
    [InlineData(double.NaN)]
    public void DipCoordinateFailsClosedForNonRepresentableInput(double dip)
    {
        bool converted = DipPixelConverter.TryDipToPixel(dip, 192, out int pixels);

        Assert.False(converted);
        Assert.Equal(0, pixels);
    }

    [Fact]
    public void ConversionsFailClosedForZeroDpi()
    {
        Assert.False(DipPixelConverter.TryDipToPixel(10d, 0, out _));
        Assert.False(DipPixelConverter.TryDipLengthToPixels(10d, 0, out _));
        Assert.False(DipPixelConverter.TryPixelToDip(10, 0, out _));
    }
}
