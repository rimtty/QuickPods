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
}
