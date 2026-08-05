using System.Runtime.InteropServices;
using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Discovery;

internal static class Win32TaskbarDiscovery
{
    private const string PrimaryTaskbarClass = "Shell_TrayWnd";
    private const int MaximumClassNameLength = 256;

    private static readonly Dictionary<string, CriticalTaskbarChildKind> CriticalChildClasses =
        new(StringComparer.Ordinal)
        {
            ["TrayNotifyWnd"] = CriticalTaskbarChildKind.NotificationArea,
            ["TrayClockWClass"] = CriticalTaskbarChildKind.Clock,
            ["ReBarWindow32"] = CriticalTaskbarChildKind.TaskbarBand,
            ["MSTaskSwWClass"] = CriticalTaskbarChildKind.TaskSwitch,
            ["MSTaskListWClass"] = CriticalTaskbarChildKind.TaskList,
            ["TaskbandHWND"] = CriticalTaskbarChildKind.TaskbarBand,
            ["Windows.UI.Composition.DesktopWindowContentBridge"] = CriticalTaskbarChildKind.XamlIsland,
        };

    internal static Win32TaskbarProbe Discover(nint ignoredWindowHandle = default)
    {
        List<TaskbarDiscoveryFault> faults = [];
        List<nint> primaryTaskbars = [];

        bool VisitWindow(nint windowHandle, nint _)
        {
            if (!TryGetClassName(windowHandle, out string className))
            {
                faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TopLevelClassReadFailed));
                return true;
            }

            if (string.Equals(className, PrimaryTaskbarClass, StringComparison.Ordinal))
            {
                primaryTaskbars.Add(windowHandle);
            }

            return true;
        }

        Marshal.SetLastPInvokeError(0);
        if (!TaskbarNativeMethods.EnumWindows(VisitWindow, 0))
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TopLevelEnumerationFailed));
        }

        if (primaryTaskbars.Count == 0)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.PrimaryTaskbarMissing));
            return new Win32TaskbarProbe(null, faults, IgnoredHostMatch: false);
        }

        if (primaryTaskbars.Count != 1)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.PrimaryTaskbarDuplicate));
            return new Win32TaskbarProbe(null, faults, IgnoredHostMatch: false);
        }

        nint taskbarHandle = primaryTaskbars[0];
        if (!TaskbarNativeMethods.IsWindowVisible(taskbarHandle))
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.PrimaryTaskbarNotVisible));
        }

        if (!TaskbarNativeMethods.GetWindowRect(taskbarHandle, out TaskbarNativeMethods.NativeRect nativeBounds))
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TaskbarBoundsUnavailable));
            return new Win32TaskbarProbe(null, faults, IgnoredHostMatch: false);
        }

        var bounds = nativeBounds.ToPixelRect();
        if (!bounds.IsValid)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TaskbarBoundsInvalid));
        }

        uint dpi = TaskbarNativeMethods.GetDpiForWindow(taskbarHandle);
        if (dpi == 0)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TaskbarDpiUnavailable));
        }

        uint threadId = TaskbarNativeMethods.GetWindowThreadProcessId(taskbarHandle, out uint processId);
        if (threadId == 0 || processId == 0)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TaskbarProcessUnavailable));
        }

        nint monitorHandle = TaskbarNativeMethods.MonitorFromWindow(
            taskbarHandle,
            TaskbarNativeMethods.MonitorDefaultToNull);
        if (monitorHandle == 0)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TaskbarMonitorUnavailable));
            return new Win32TaskbarProbe(null, faults, IgnoredHostMatch: false);
        }

        var nativeMonitor = new TaskbarNativeMethods.NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<TaskbarNativeMethods.NativeMonitorInfo>(),
        };
        if (!TaskbarNativeMethods.GetMonitorInfo(monitorHandle, ref nativeMonitor))
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TaskbarMonitorInfoUnavailable));
            return new Win32TaskbarProbe(null, faults, IgnoredHostMatch: false);
        }

        bool isPrimary = (nativeMonitor.Flags & TaskbarNativeMethods.MonitorInfoPrimary) != 0;
        if (!isPrimary)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TaskbarNotOnPrimaryMonitor));
        }

        var monitorBounds = nativeMonitor.Monitor.ToPixelRect();
        if (!monitorBounds.IsValid)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TaskbarMonitorBoundsInvalid));
        }

        var monitorWorkArea = nativeMonitor.WorkArea.ToPixelRect();
        if (!monitorWorkArea.IsValid)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TaskbarMonitorWorkAreaInvalid));
        }

        if (!bounds.Intersects(monitorBounds))
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.TaskbarOutsideMonitor));
        }

        TaskbarMonitorSnapshot monitor = new(monitorBounds, monitorWorkArea, isPrimary);
        Win32CriticalChildrenProbe criticalChildren = DiscoverCriticalChildren(
            taskbarHandle,
            bounds,
            ignoredWindowHandle,
            faults);

        Win32TaskbarTarget target = new(
            taskbarHandle,
            processId,
            bounds,
            dpi,
            monitor,
            criticalChildren.Children);
        return new Win32TaskbarProbe(target, faults, criticalChildren.IgnoredHostMatch);
    }

    private static Win32CriticalChildrenProbe DiscoverCriticalChildren(
        nint taskbarHandle,
        PixelRect taskbarBounds,
        nint ignoredWindowHandle,
        List<TaskbarDiscoveryFault> faults)
    {
        List<CriticalTaskbarChildSnapshot> children = [];
        bool ignoredHostMatch = false;
        bool VisitChild(nint windowHandle, nint _)
        {
            if (TaskbarNativeChildPolicy.ShouldIgnoreAndRecordMatch(
                    windowHandle,
                    ignoredWindowHandle,
                    ref ignoredHostMatch))
            {
                return true;
            }

            if (!TaskbarNativeMethods.IsWindowVisible(windowHandle))
            {
                return true;
            }

            if (!TryGetClassName(windowHandle, out string className))
            {
                faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.ChildClassReadFailed));
                return true;
            }

            CriticalTaskbarChildKind kind = CriticalChildClasses.TryGetValue(
                className,
                out CriticalTaskbarChildKind knownKind)
                ? knownKind
                : CriticalTaskbarChildKind.UnknownObstacle;

            if (!TaskbarNativeMethods.GetWindowRect(windowHandle, out TaskbarNativeMethods.NativeRect nativeBounds))
            {
                faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.CriticalChildBoundsUnavailable));
                return true;
            }

            var bounds = nativeBounds.ToPixelRect();
            if (!bounds.IsValid)
            {
                long width = (long)nativeBounds.Right - nativeBounds.Left;
                long height = (long)nativeBounds.Bottom - nativeBounds.Top;
                if (kind == CriticalTaskbarChildKind.UnknownObstacle &&
                    (width == 0 || height == 0))
                {
                    // A visible-style HWND with no pixel area cannot intersect a
                    // candidate. Windows 11 keeps such implementation-detail
                    // descendants in the taskbar tree during normal operation.
                    return true;
                }

                faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.CriticalChildBoundsInvalid));
                return true;
            }

            if (!bounds.Intersects(taskbarBounds))
            {
                faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.CriticalChildOutsideTaskbar));
            }

            children.Add(new CriticalTaskbarChildSnapshot(kind, bounds));
            return true;
        }

        Marshal.SetLastPInvokeError(0);
        if (!TaskbarNativeMethods.EnumChildWindows(taskbarHandle, VisitChild, 0))
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.ChildEnumerationFailed));
        }

        int notificationAreaCount = children.Count(
            child => child.Kind == CriticalTaskbarChildKind.NotificationArea);
        if (notificationAreaCount == 0)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.NotificationAreaMissing));
        }
        else if (notificationAreaCount > 1)
        {
            faults.Add(new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.NotificationAreaDuplicate));
        }

        return new Win32CriticalChildrenProbe(children, ignoredHostMatch);
    }

    private static bool TryGetClassName(nint windowHandle, out string className)
    {
        char[] buffer = new char[MaximumClassNameLength];
        int length = TaskbarNativeMethods.GetClassName(windowHandle, buffer, buffer.Length);
        if (length <= 0)
        {
            className = string.Empty;
            return false;
        }

        className = new string(buffer, 0, length);
        return true;
    }
}

internal static class TaskbarNativeChildPolicy
{
    /// <summary>
    /// A visible watchdog scan must not turn the already verified QuickPods
    /// HWND into an unknown obstacle against itself. Only the exact live handle
    /// supplied by the host runner is excluded; every other native descendant
    /// remains part of the fail-closed obstacle observation.
    /// </summary>
    internal static bool ShouldIgnore(nint candidate, nint ignoredWindowHandle) =>
        ignoredWindowHandle != nint.Zero && candidate == ignoredWindowHandle;

    /// <summary>
    /// Records only whether the exact ignored HWND was encountered. The raw
    /// value is neither copied into discovery results nor diagnostic output.
    /// </summary>
    internal static bool ShouldIgnoreAndRecordMatch(
        nint candidate,
        nint ignoredWindowHandle,
        ref bool ignoredHostMatch)
    {
        bool shouldIgnore = ShouldIgnore(candidate, ignoredWindowHandle);
        ignoredHostMatch |= shouldIgnore;
        return shouldIgnore;
    }
}

internal sealed record Win32CriticalChildrenProbe(
    IReadOnlyList<CriticalTaskbarChildSnapshot> Children,
    bool IgnoredHostMatch);

internal sealed class Win32TaskbarTarget
{
    internal Win32TaskbarTarget(
        nint taskbarHandle,
        uint explorerProcessId,
        PixelRect bounds,
        uint dpi,
        TaskbarMonitorSnapshot monitor,
        IReadOnlyList<CriticalTaskbarChildSnapshot> criticalChildren)
    {
        TaskbarHandle = taskbarHandle;
        ExplorerProcessId = explorerProcessId;
        Bounds = bounds;
        Dpi = dpi;
        Monitor = monitor;
        CriticalChildren = criticalChildren;
    }

    internal nint TaskbarHandle { get; }

    internal uint ExplorerProcessId { get; }

    internal PixelRect Bounds { get; }

    internal uint Dpi { get; }

    internal TaskbarMonitorSnapshot Monitor { get; }

    internal IReadOnlyList<CriticalTaskbarChildSnapshot> CriticalChildren { get; }
}

internal sealed record Win32TaskbarProbe(
    Win32TaskbarTarget? Target,
    IReadOnlyList<TaskbarDiscoveryFault> Faults,
    bool IgnoredHostMatch);
