namespace QuickPods.Spike.TaskbarHost.Geometry;

/// <summary>
/// Checked conversions between DIP (96 units per logical inch) and physical pixels.
/// </summary>
internal static class DipPixelConverter
{
    private const double DipPerLogicalInch = 96d;

    public static bool TryDipToPixel(double dip, uint dpi, out int pixel)
    {
        return TryScaleAndRound(dip, dpi, static value => Math.Round(value, MidpointRounding.AwayFromZero), out pixel);
    }

    public static bool TryDipLengthToPixels(double dipLength, uint dpi, out int pixelLength)
    {
        pixelLength = 0;

        if (!(dipLength > 0d))
        {
            return false;
        }

        return TryScaleAndRound(dipLength, dpi, Math.Ceiling, out pixelLength) && pixelLength > 0;
    }

    public static bool TryPixelToDip(int pixel, uint dpi, out double dip)
    {
        dip = 0d;

        if (dpi == 0)
        {
            return false;
        }

        dip = pixel * DipPerLogicalInch / dpi;
        return double.IsFinite(dip);
    }

    /// <summary>
    /// Converts a DIP rectangle conservatively: leading edges round down and trailing
    /// edges round up so the physical rectangle never understates the DIP extent.
    /// </summary>
    public static bool TryDipRectToPixels(DipRect dipRectangle, uint dpi, out PixelRect pixelRectangle)
    {
        pixelRectangle = default;

        if (!dipRectangle.IsValid ||
            !TryScaleAndRound(dipRectangle.Left, dpi, Math.Floor, out int left) ||
            !TryScaleAndRound(dipRectangle.Top, dpi, Math.Floor, out int top) ||
            !TryScaleAndRound(dipRectangle.Right, dpi, Math.Ceiling, out int right) ||
            !TryScaleAndRound(dipRectangle.Bottom, dpi, Math.Ceiling, out int bottom))
        {
            return false;
        }

        pixelRectangle = new PixelRect(left, top, right, bottom);
        return pixelRectangle.IsValid;
    }

    public static bool TryPixelRectToDips(PixelRect pixelRectangle, uint dpi, out DipRect dipRectangle)
    {
        dipRectangle = default;

        if (!pixelRectangle.IsValid ||
            !TryPixelToDip(pixelRectangle.Left, dpi, out double left) ||
            !TryPixelToDip(pixelRectangle.Top, dpi, out double top) ||
            !TryPixelToDip(pixelRectangle.Right, dpi, out double right) ||
            !TryPixelToDip(pixelRectangle.Bottom, dpi, out double bottom))
        {
            return false;
        }

        dipRectangle = new DipRect(left, top, right, bottom);
        return dipRectangle.IsValid;
    }

    private static bool TryScaleAndRound(
        double dip,
        uint dpi,
        Func<double, double> round,
        out int pixel)
    {
        pixel = 0;

        if (!double.IsFinite(dip) || dpi == 0)
        {
            return false;
        }

        double scaled = dip * dpi / DipPerLogicalInch;
        if (!double.IsFinite(scaled))
        {
            return false;
        }

        double rounded = round(scaled);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
        {
            return false;
        }

        pixel = (int)rounded;
        return true;
    }
}
