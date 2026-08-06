using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Placement;

namespace QuickPods.TaskbarHost.Presentation;

internal enum TaskbarPresentationSurface
{
    Native,
    Floating,
    Hidden,
}

internal enum TaskbarPresentationReason
{
    NativePlacement,
    VerifiedNoFitFloating,
    TransientUnknownHidden,
    InconsistentEvidenceHidden,
    FloatingPlacementUnavailableHidden,
}

internal sealed record TaskbarPresentationRoute(
    TaskbarPresentationSurface Surface,
    PixelRect? Bounds,
    PixelRect? VerifiedWorkArea,
    uint Dpi,
    TaskbarPresentationReason Reason);

internal static class TaskbarPresentationRouter
{
    internal static TaskbarPresentationRoute Select(
        TaskbarDiscoveryResult discovery,
        TaskbarPlacementResult placement,
        PixelRect? priorNativeBounds = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(placement);

        if (placement.Decision == PlacementDecision.Place)
        {
            if (discovery.IsComplete &&
                discovery.Snapshot is LiveTaskbarSnapshot snapshot &&
                placement.Bounds is PixelRect bounds &&
                bounds.IsValid &&
                snapshot.Bounds.Contains(bounds) &&
                snapshot.Dpi != 0)
            {
                return new(
                    TaskbarPresentationSurface.Native,
                    bounds,
                    snapshot.WorkArea,
                    snapshot.Dpi,
                    TaskbarPresentationReason.NativePlacement);
            }

            return Hidden(TaskbarPresentationReason.InconsistentEvidenceHidden);
        }

        if (placement.Decision == PlacementDecision.VerifiedNoFit)
        {
            if (!discovery.IsComplete || discovery.Snapshot is not LiveTaskbarSnapshot snapshot)
            {
                return Hidden(TaskbarPresentationReason.InconsistentEvidenceHidden);
            }

            FloatingPlacementResult floating = FloatingPlacementCalculator.Calculate(
                new(snapshot.WorkArea, snapshot.Dpi, priorNativeBounds));
            if (floating.IsAvailable && floating.Bounds is PixelRect bounds)
            {
                return new(
                    TaskbarPresentationSurface.Floating,
                    bounds,
                    snapshot.WorkArea,
                    snapshot.Dpi,
                    TaskbarPresentationReason.VerifiedNoFitFloating);
            }

            return Hidden(TaskbarPresentationReason.FloatingPlacementUnavailableHidden);
        }

        return Hidden(TaskbarPresentationReason.TransientUnknownHidden);
    }

    private static TaskbarPresentationRoute Hidden(TaskbarPresentationReason reason) =>
        new(TaskbarPresentationSurface.Hidden, null, null, 0, reason);
}
