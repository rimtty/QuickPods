using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Diagnostics;

internal static class TaskbarLayoutAdapter
{
    private const string StartButtonAutomationId = "StartButton";
    private const string WidgetsButtonAutomationId = "WidgetsButton";

    public static TaskbarLayoutObservation? CreateObservation(TaskbarDiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        if (discovery.Snapshot is not TaskbarSnapshot snapshot)
        {
            return null;
        }

        PixelRect? start = FindUniqueButton(snapshot.AutomationButtons, StartButtonAutomationId);
        PixelRect? widgets = FindUniqueButton(snapshot.AutomationButtons, WidgetsButtonAutomationId);

        var obstacles = new List<PixelRect>(snapshot.AutomationButtons.Count + snapshot.CriticalChildren.Count);
        obstacles.AddRange(snapshot.AutomationButtons.Select(static button => button.Bounds));
        obstacles.AddRange(
            snapshot.CriticalChildren
                .Where(static child => child.Kind is
                    CriticalTaskbarChildKind.NotificationArea or
                    CriticalTaskbarChildKind.Clock or
                    CriticalTaskbarChildKind.UnknownObstacle)
                .Select(static child => child.Bounds));

        TaskbarOrientation orientation = snapshot.Bounds.Width > snapshot.Bounds.Height
            ? TaskbarOrientation.Horizontal
            : snapshot.Bounds.Height > snapshot.Bounds.Width
                ? TaskbarOrientation.Vertical
                : TaskbarOrientation.Unknown;

        return new TaskbarLayoutObservation(
            snapshot.Bounds,
            snapshot.Dpi,
            orientation,
            start,
            widgets,
            obstacles,
            discovery.IsComplete && start is not null);
    }

    /// <summary>
    /// Conservatively adds a fresh native-child snapshot to the last complete
    /// UIA observation for bounded visible-continuity admission. Stale native
    /// obstacles are intentionally retained; the subsequent full scan replaces
    /// the entire observation before the surface may remain visible.
    /// </summary>
    internal static TaskbarLayoutObservation? CreateNativePreflightObservation(
        TaskbarLayoutObservation? previous,
        IReadOnlyList<CriticalTaskbarChildSnapshot>? freshCriticalChildren)
    {
        if (previous?.Obstacles is null || freshCriticalChildren is null)
        {
            return null;
        }

        IReadOnlyList<PixelRect> freshObstacles =
        [
            .. freshCriticalChildren
                .Where(static child => IsConcreteCriticalObstacle(child.Kind))
                .Select(static child => child.Bounds),
        ];
        PixelRect[] mergedObstacles =
        [
            .. previous.Obstacles
                .Concat(freshObstacles)
                .Distinct(),
        ];
        return previous with { Obstacles = mergedObstacles };
    }

    internal static TaskbarLayoutObservation? CreateConservativeContinuityObservation(
        TaskbarLayoutObservation? fresh,
        TaskbarLayoutObservation? previous)
    {
        if (fresh?.Obstacles is null || previous?.Obstacles is null)
        {
            return null;
        }

        PixelRect[] mergedObstacles =
        [
            .. fresh.Obstacles
                .Concat(previous.Obstacles)
                .Distinct(),
        ];
        return fresh with
        {
            Obstacles = mergedObstacles,
            IsComplete = fresh.IsComplete && previous.IsComplete,
        };
    }

    private static PixelRect? FindUniqueButton(
        IReadOnlyList<AutomationButtonSnapshot> buttons,
        string automationId)
    {
        PixelRect? match = null;
        foreach (AutomationButtonSnapshot button in buttons)
        {
            if (!string.Equals(button.AutomationId, automationId, StringComparison.Ordinal))
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = button.Bounds;
        }

        return match;
    }

    private static bool IsConcreteCriticalObstacle(CriticalTaskbarChildKind kind) =>
        kind is
            CriticalTaskbarChildKind.NotificationArea or
            CriticalTaskbarChildKind.Clock or
            CriticalTaskbarChildKind.UnknownObstacle;
}
