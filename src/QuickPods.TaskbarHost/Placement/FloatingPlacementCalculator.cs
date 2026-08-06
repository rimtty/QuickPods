using QuickPods.TaskbarHost.Geometry;

namespace QuickPods.TaskbarHost.Placement;

/// <summary>
/// Calculates a bottom-aligned top-level fallback using only a verified
/// primary-monitor work area. It does not inspect or modify Windows state.
/// </summary>
internal static class FloatingPlacementCalculator
{
    internal const uint MinimumSupportedDpi = 96;
    internal const uint MaximumSupportedDpi = 240;

    private const double WidthDip = 300d;
    private const double HeightDip = 40d;
    private const double MarginDip = 8d;

    internal static FloatingPlacementResult Calculate(FloatingPlacementInput input)
    {
        PixelRect workArea = input.PrimaryMonitorWorkArea;
        if (!workArea.IsValid)
        {
            return FloatingPlacementResult.Unavailable(
                FloatingPlacementUnavailableReason.InvalidWorkArea);
        }

        if (input.Dpi < MinimumSupportedDpi || input.Dpi > MaximumSupportedDpi)
        {
            return FloatingPlacementResult.Unavailable(
                FloatingPlacementUnavailableReason.UnsupportedDpi);
        }

        if (input.PriorNativeBounds is PixelRect priorNativeBounds && !priorNativeBounds.IsValid)
        {
            return FloatingPlacementResult.Unavailable(
                FloatingPlacementUnavailableReason.InvalidPriorNativeBounds);
        }

        if (!DpiPixels.TryConvertLength(WidthDip, input.Dpi, out int width) ||
            !DpiPixels.TryConvertLength(HeightDip, input.Dpi, out int height) ||
            !DpiPixels.TryConvertLength(MarginDip, input.Dpi, out int margin))
        {
            return FloatingPlacementResult.Unavailable(
                FloatingPlacementUnavailableReason.MetricConversionFailed);
        }

        long requiredWidth = (long)width + (2L * margin);
        long requiredHeight = (long)height + margin;
        if (workArea.Width < requiredWidth)
        {
            return FloatingPlacementResult.Unavailable(
                FloatingPlacementUnavailableReason.InsufficientWidth);
        }

        if (workArea.Height < requiredHeight)
        {
            return FloatingPlacementResult.Unavailable(
                FloatingPlacementUnavailableReason.InsufficientHeight);
        }

        long minimumLeft = (long)workArea.Left + margin;
        long maximumLeft = (long)workArea.Right - margin - width;
        long preferredCenterTwice = input.PriorNativeBounds is PixelRect prior
            ? (long)prior.Left + prior.Right
            : (long)workArea.Left + workArea.Right;
        long preferredLeft = FloorDivideByTwo(preferredCenterTwice - width);
        long left = Math.Clamp(preferredLeft, minimumLeft, maximumLeft);
        long top = (long)workArea.Bottom - margin - height;

        if (left < int.MinValue || left > int.MaxValue ||
            top < int.MinValue || top > int.MaxValue ||
            !PixelRect.TryFromPositionAndSize(
                (int)left,
                (int)top,
                width,
                height,
                out PixelRect placement) ||
            !workArea.Contains(placement))
        {
            return FloatingPlacementResult.Unavailable(
                FloatingPlacementUnavailableReason.CoordinateOverflow);
        }

        return FloatingPlacementResult.Place(placement);
    }

    private static long FloorDivideByTwo(long value) =>
        value >= 0 ? value / 2 : (value - 1) / 2;
}
