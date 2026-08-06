namespace QuickPods.TaskbarHost.Geometry;

internal static class DpiPixels
{
    private const double DefaultDpi = 96d;

    public static bool TryConvertLength(double dip, uint dpi, out int pixels)
    {
        pixels = 0;
        if (!double.IsFinite(dip) || dip <= 0d || dpi == 0)
        {
            return false;
        }

        double scaled = dip * dpi / DefaultDpi;
        if (!double.IsFinite(scaled) || scaled > int.MaxValue)
        {
            return false;
        }

        pixels = (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        return pixels > 0;
    }

    public static bool TryConvertNonNegativeLength(double dip, uint dpi, out int pixels)
    {
        if (dip == 0d && dpi != 0)
        {
            pixels = 0;
            return true;
        }

        return TryConvertLength(dip, dpi, out pixels);
    }
}
