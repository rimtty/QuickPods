using QuickPods.TaskbarHost.Geometry;

namespace QuickPods.TaskbarHost.Placement;

internal enum FloatingPlacementUnavailableReason
{
    None,
    UnsupportedDpi,
    InvalidWorkArea,
    InvalidPriorNativeBounds,
    MetricConversionFailed,
    InsufficientWidth,
    InsufficientHeight,
    CoordinateOverflow,
}

internal readonly record struct FloatingPlacementInput(
    PixelRect PrimaryMonitorWorkArea,
    uint Dpi,
    PixelRect? PriorNativeBounds = null);

internal sealed record FloatingPlacementResult(
    PixelRect? Bounds,
    FloatingPlacementUnavailableReason UnavailableReason)
{
    internal bool IsAvailable =>
        Bounds is PixelRect bounds &&
        bounds.IsValid &&
        UnavailableReason == FloatingPlacementUnavailableReason.None;

    internal static FloatingPlacementResult Place(PixelRect bounds)
    {
        if (!bounds.IsValid)
        {
            throw new ArgumentException("Floating placement bounds must be valid.", nameof(bounds));
        }

        return new(bounds, FloatingPlacementUnavailableReason.None);
    }

    internal static FloatingPlacementResult Unavailable(FloatingPlacementUnavailableReason reason)
    {
        if (reason == FloatingPlacementUnavailableReason.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "An unavailable placement requires a failure reason.");
        }

        return new(null, reason);
    }
}
