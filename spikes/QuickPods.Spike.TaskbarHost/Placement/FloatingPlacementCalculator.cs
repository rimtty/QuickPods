using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Placement;

/// <summary>
/// Calculates a bottom-aligned top-level fallback placement using only a
/// verified primary-monitor work area. It does not inspect the taskbar or call
/// Windows APIs.
/// </summary>
internal static class FloatingPlacementCalculator
{
    internal const uint MinimumSupportedDpi = 96;

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

        if (input.Dpi < MinimumSupportedDpi)
        {
            return FloatingPlacementResult.Unavailable(
                FloatingPlacementUnavailableReason.UnsupportedDpi);
        }

        if (input.PriorNativeBounds is PixelRect priorNativeBounds &&
            !priorNativeBounds.IsValid)
        {
            return FloatingPlacementResult.Unavailable(
                FloatingPlacementUnavailableReason.InvalidPriorNativeBounds);
        }

        if (!DipPixelConverter.TryDipLengthToPixels(WidthDip, input.Dpi, out int width) ||
            !DipPixelConverter.TryDipLengthToPixels(HeightDip, input.Dpi, out int height) ||
            !DipPixelConverter.TryDipLengthToPixels(MarginDip, input.Dpi, out int margin))
        {
            return FloatingPlacementResult.Unavailable(
                FloatingPlacementUnavailableReason.MetricConversionFailed);
        }

        long requiredWidth = checked((long)width + (2L * margin));
        long requiredHeight = checked((long)height + margin);
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

        long minimumLeft = checked((long)workArea.Left + margin);
        long maximumLeft = checked((long)workArea.Right - margin - width);
        long preferredCenterTwice = input.PriorNativeBounds is PixelRect prior
            ? checked((long)prior.Left + prior.Right)
            : checked((long)workArea.Left + workArea.Right);
        long preferredLeft = FloorDivideByTwo(checked(preferredCenterTwice - width));
        long left = Math.Clamp(preferredLeft, minimumLeft, maximumLeft);
        long top = checked((long)workArea.Bottom - margin - height);

        if (left < int.MinValue || left > int.MaxValue ||
            top < int.MinValue || top > int.MaxValue ||
            !PixelRect.TryCreateFromPositionAndSize(
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
