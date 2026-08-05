using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Placement;

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

/// <summary>
/// Physical-pixel inputs for the top-level floating strip. A prior native
/// placement contributes only its horizontal center preference.
/// </summary>
internal readonly record struct FloatingPlacementInput(
    PixelRect PrimaryMonitorWorkArea,
    uint Dpi,
    PixelRect? PriorNativeBounds = null);

/// <summary>
/// A verified physical-pixel placement or an explicit reason that placement
/// is unavailable. Unavailable results never carry guessed bounds.
/// </summary>
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

    internal static FloatingPlacementResult Unavailable(
        FloatingPlacementUnavailableReason reason)
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
