using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using QuickPods.TaskbarHost.Geometry;

namespace QuickPods.TaskbarHost.Discovery;

internal static class TaskbarAutomationDiscovery
{
    private const string StartButtonAutomationId = "StartButton";
    private const string WidgetsButtonAutomationId = "WidgetsButton";
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(5);

    public static async Task<AutomationTaskbarProbe> DiscoverAsync(
        nint taskbarHandle,
        PixelRect taskbarBounds,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AutomationMta.RunAsync(
                () => DiscoverCore(taskbarHandle, taskbarBounds),
                ScanTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return Failed(TaskbarDiscoveryFault.AutomationTimedOut);
        }
        catch (Exception exception) when (IsAutomationFailure(exception))
        {
            return Failed(TaskbarDiscoveryFault.AutomationWorkerFailed);
        }
    }

    private static AutomationTaskbarProbe DiscoverCore(nint taskbarHandle, PixelRect taskbarBounds)
    {
        var buttons = new List<AutomationButtonSnapshot>();
        var faults = new HashSet<TaskbarDiscoveryFault>();
        AutomationElement root;
        try
        {
            root = AutomationElement.FromHandle(taskbarHandle);
        }
        catch (Exception exception) when (IsAutomationFailure(exception))
        {
            return Failed(TaskbarDiscoveryFault.AutomationRootUnavailable);
        }

        if (root is null)
        {
            return Failed(TaskbarDiscoveryFault.AutomationRootUnavailable);
        }

        AutomationElementCollection candidates;
        try
        {
            var condition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button);
            candidates = root.FindAll(TreeScope.Descendants, condition);
        }
        catch (Exception exception) when (IsAutomationFailure(exception))
        {
            return Failed(TaskbarDiscoveryFault.AutomationEnumerationFailed);
        }

        for (int index = 0; index < candidates.Count; index++)
        {
            try
            {
                AutomationElement element = candidates[index];
                if (element.Current.IsOffscreen)
                {
                    continue;
                }

                if (!TryConvertRectangle(element.Current.BoundingRectangle, out PixelRect bounds))
                {
                    faults.Add(TaskbarDiscoveryFault.AutomationBoundsInvalid);
                    continue;
                }

                if (!taskbarBounds.Contains(bounds))
                {
                    faults.Add(TaskbarDiscoveryFault.AutomationButtonOutsideTaskbar);
                    continue;
                }

                buttons.Add(new AutomationButtonSnapshot(
                    element.Current.AutomationId ?? string.Empty,
                    bounds));
            }
            catch (Exception exception) when (IsAutomationFailure(exception))
            {
                faults.Add(TaskbarDiscoveryFault.AutomationPropertyUnavailable);
            }
        }

        AddIdentityFaults(buttons, faults);
        return new AutomationTaskbarProbe(buttons, [.. faults]);
    }

    private static void AddIdentityFaults(
        IReadOnlyCollection<AutomationButtonSnapshot> buttons,
        HashSet<TaskbarDiscoveryFault> faults)
    {
        int startCount = buttons.Count(
            button => string.Equals(button.AutomationId, StartButtonAutomationId, StringComparison.Ordinal));
        if (startCount == 0)
        {
            faults.Add(TaskbarDiscoveryFault.StartButtonMissing);
        }
        else if (startCount > 1)
        {
            faults.Add(TaskbarDiscoveryFault.StartButtonDuplicate);
        }

        int widgetsCount = buttons.Count(
            button => string.Equals(button.AutomationId, WidgetsButtonAutomationId, StringComparison.Ordinal));
        if (widgetsCount > 1)
        {
            faults.Add(TaskbarDiscoveryFault.WidgetsButtonDuplicate);
        }
    }

    private static bool TryConvertRectangle(Rect source, out PixelRect result)
    {
        double left = Math.Floor(source.Left);
        double top = Math.Floor(source.Top);
        double right = Math.Ceiling(source.Right);
        double bottom = Math.Ceiling(source.Bottom);
        if (!IsCoordinate(left) || !IsCoordinate(top) || !IsCoordinate(right) || !IsCoordinate(bottom))
        {
            result = default;
            return false;
        }

        result = new PixelRect((int)left, (int)top, (int)right, (int)bottom);
        return result.IsValid;
    }

    private static bool IsCoordinate(double value) =>
        double.IsFinite(value) && value >= int.MinValue && value <= int.MaxValue;

    private static bool IsAutomationFailure(Exception exception) =>
        exception is COMException or
            ElementNotAvailableException or
            InvalidOperationException or
            ArgumentException or
            UnauthorizedAccessException or
            NotSupportedException;

    private static AutomationTaskbarProbe Failed(TaskbarDiscoveryFault fault) =>
        new([], [fault]);
}
