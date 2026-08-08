using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Placement;

namespace QuickPods.TaskbarHost.Discovery;

internal static class TaskbarObservationAdapter
{
    private const string StartButtonAutomationId = "StartButton";
    private const string WidgetsButtonAutomationId = "WidgetsButton";

    public static TaskbarLayoutObservation? Create(TaskbarDiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        if (discovery.Snapshot is not LiveTaskbarSnapshot snapshot)
        {
            return null;
        }

        PixelRect? start = FindUnique(snapshot.AutomationButtons, StartButtonAutomationId);
        PixelRect? widgets = FindUnique(snapshot.AutomationButtons, WidgetsButtonAutomationId);
        PixelRect? notificationArea = FindUnique(
            snapshot.NativeObstacles,
            NativeObstacleKind.NotificationArea);
        PixelRect[] obstacles =
        [
            .. snapshot.AutomationButtons.Select(button => button.Bounds),
            .. snapshot.NativeObstacles.Select(obstacle => obstacle.Bounds),
        ];
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
            notificationArea,
            [.. obstacles.Distinct()],
            discovery.IsComplete && start is not null && notificationArea is not null);
    }

    private static PixelRect? FindUnique(
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

    private static PixelRect? FindUnique(
        IReadOnlyList<NativeTaskbarObstacle> obstacles,
        NativeObstacleKind kind)
    {
        PixelRect? match = null;
        foreach (NativeTaskbarObstacle obstacle in obstacles)
        {
            if (obstacle.Kind != kind)
            {
                continue;
            }

            if (match is not null)
            {
                return null;
            }

            match = obstacle.Bounds;
        }

        return match;
    }
}
