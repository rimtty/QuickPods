using QuickPods.TaskbarHost.Geometry;

namespace QuickPods.TaskbarHost.Placement;

internal enum TaskbarOrientation
{
    Unknown,
    Horizontal,
    Vertical,
}

internal enum PlacementDecision
{
    Place,
    VerifiedNoFit,
    UnsupportedConfiguration,
    TransientUnknown,
}

internal enum TaskbarStripMode
{
    Standard,
    Compact,
}

internal enum PlacementReason
{
    None,
    IncompleteObservation,
    UnsupportedOrientation,
    UnsupportedAlignment,
    InvalidGeometry,
    ContradictoryLandmarks,
    ConversionFailed,
    InsufficientHeight,
    InsufficientWidth,
    VerificationFailed,
}

internal readonly record struct TaskbarPlacementOptions(
    double StandardWidthDip,
    double CompactMinimumWidthDip,
    double HeightDip,
    double MarginDip)
{
    public static TaskbarPlacementOptions Default { get; } = new(300d, 190d, 40d, 8d);

    public bool IsValid =>
        double.IsFinite(StandardWidthDip) &&
        double.IsFinite(CompactMinimumWidthDip) &&
        double.IsFinite(HeightDip) &&
        double.IsFinite(MarginDip) &&
        StandardWidthDip >= CompactMinimumWidthDip &&
        CompactMinimumWidthDip > 0d &&
        HeightDip > 0d &&
        MarginDip >= 0d;
}

internal sealed record TaskbarLayoutObservation(
    PixelRect TaskbarBounds,
    uint Dpi,
    TaskbarOrientation Orientation,
    PixelRect? StartButtonBounds,
    PixelRect? WidgetsButtonBounds,
    IReadOnlyList<PixelRect> Obstacles,
    bool IsComplete);

internal sealed record TaskbarPlacementResult(
    PlacementDecision Decision,
    PixelRect? Bounds,
    TaskbarStripMode? Mode,
    PlacementReason Reason)
{
    public static TaskbarPlacementResult Place(PixelRect bounds, TaskbarStripMode mode) =>
        new(PlacementDecision.Place, bounds, mode, PlacementReason.None);

    public static TaskbarPlacementResult VerifiedNoFit(PlacementReason reason) =>
        new(PlacementDecision.VerifiedNoFit, null, null, reason);

    public static TaskbarPlacementResult UnsupportedConfiguration(PlacementReason reason) =>
        new(PlacementDecision.UnsupportedConfiguration, null, null, reason);

    public static TaskbarPlacementResult TransientUnknown(PlacementReason reason) =>
        new(PlacementDecision.TransientUnknown, null, null, reason);
}
