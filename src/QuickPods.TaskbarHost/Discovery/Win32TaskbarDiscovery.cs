using System.Runtime.InteropServices;
using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Interop;

namespace QuickPods.TaskbarHost.Discovery;

internal static class Win32TaskbarDiscovery
{
    internal const string HostViewClassName = "QuickPods.Taskbar.View";

    private const string PrimaryTaskbarClassName = "Shell_TrayWnd";
    private const string NotificationAreaClassName = "TrayNotifyWnd";
    private const string ClockClassName = "TrayClockWClass";
    private const int MaximumClassNameLength = 256;

    public static NativeTaskbarProbe Discover(nint ignoredHost = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Failed(TaskbarDiscoveryFault.UnsupportedPlatform);
        }

        var taskbars = new List<nint>();
        var faults = new HashSet<TaskbarDiscoveryFault>();
        TaskbarNativeMethods.EnumWindowProc callback = (windowHandle, _) =>
        {
            if (!TryReadClassName(windowHandle, out string className))
            {
                if (TaskbarNativeMethods.IsWindow(windowHandle))
                {
                    faults.Add(TaskbarDiscoveryFault.TopLevelClassUnavailable);
                }

                return true;
            }

            if (string.Equals(className, PrimaryTaskbarClassName, StringComparison.Ordinal))
            {
                taskbars.Add(windowHandle);
            }

            return true;
        };

        if (!TaskbarNativeMethods.EnumWindows(callback, nint.Zero))
        {
            faults.Add(TaskbarDiscoveryFault.TopLevelEnumerationFailed);
        }

        if (taskbars.Count == 0)
        {
            faults.Add(TaskbarDiscoveryFault.PrimaryTaskbarMissing);
            return Empty(faults);
        }

        if (taskbars.Count != 1)
        {
            faults.Add(TaskbarDiscoveryFault.PrimaryTaskbarDuplicate);
            return Empty(faults);
        }

        nint taskbar = taskbars[0];
        if (!TaskbarNativeMethods.IsWindow(taskbar) ||
            TaskbarNativeMethods.GetAncestor(taskbar, TaskbarNativeMethods.GetAncestorRoot) != taskbar)
        {
            faults.Add(TaskbarDiscoveryFault.PrimaryTaskbarInvalid);
        }

        if (!TaskbarNativeMethods.IsWindowVisible(taskbar))
        {
            faults.Add(TaskbarDiscoveryFault.PrimaryTaskbarHidden);
        }

        int cloakResult = TaskbarNativeMethods.DwmGetWindowAttribute(
            taskbar,
            TaskbarNativeMethods.DwmWindowAttributeCloaked,
            out uint cloakState,
            sizeof(uint));
        if (cloakResult != 0 || cloakState != 0)
        {
            faults.Add(TaskbarDiscoveryFault.PrimaryTaskbarCloaked);
        }

        if (!TaskbarNativeMethods.GetWindowRect(taskbar, out TaskbarNativeMethods.NativeRect nativeBounds) ||
            !nativeBounds.ToPixelRect().IsValid)
        {
            faults.Add(TaskbarDiscoveryFault.TaskbarBoundsUnavailable);
            return Empty(faults);
        }

        PixelRect bounds = nativeBounds.ToPixelRect();
        uint dpi = TaskbarNativeMethods.GetDpiForWindow(taskbar);
        if (dpi == 0)
        {
            faults.Add(TaskbarDiscoveryFault.TaskbarDpiUnavailable);
        }

        uint threadId = TaskbarNativeMethods.GetWindowThreadProcessId(taskbar, out uint processId);
        if (threadId == 0 || processId == 0)
        {
            faults.Add(TaskbarDiscoveryFault.TaskbarProcessUnavailable);
        }

        nint monitor = TaskbarNativeMethods.MonitorFromWindow(
            taskbar,
            TaskbarNativeMethods.MonitorDefaultToNull);
        var monitorInfo = new TaskbarNativeMethods.NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<TaskbarNativeMethods.NativeMonitorInfo>(),
        };
        if (monitor == nint.Zero || !TaskbarNativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            faults.Add(TaskbarDiscoveryFault.TaskbarMonitorUnavailable);
            return Empty(faults);
        }

        PixelRect monitorBounds = monitorInfo.Monitor.ToPixelRect();
        PixelRect workArea = monitorInfo.WorkArea.ToPixelRect();
        if (!monitorBounds.IsValid || !workArea.IsValid || !bounds.Intersects(monitorBounds))
        {
            faults.Add(TaskbarDiscoveryFault.TaskbarMonitorUnavailable);
        }

        if ((monitorInfo.Flags & TaskbarNativeMethods.MonitorInfoPrimary) == 0)
        {
            faults.Add(TaskbarDiscoveryFault.TaskbarNotOnPrimaryMonitor);
        }

        ChildProbe children = DiscoverChildren(taskbar, bounds, ignoredHost);
        faults.UnionWith(children.Faults);
        return new NativeTaskbarProbe(
            taskbar,
            processId,
            bounds,
            dpi,
            monitorBounds,
            workArea,
            children.Obstacles,
            [.. faults],
            children.IgnoredHostMatched);
    }

    private static ChildProbe DiscoverChildren(nint taskbar, PixelRect taskbarBounds, nint ignoredHost)
    {
        var obstacles = new List<NativeTaskbarObstacle>();
        var faults = new HashSet<TaskbarDiscoveryFault>();
        bool ignoredHostMatched = false;
        TaskbarNativeMethods.EnumWindowProc callback = (windowHandle, _) =>
        {
            if (windowHandle == ignoredHost)
            {
                if (IsTrustedHostExclusion(windowHandle, taskbar))
                {
                    ignoredHostMatched = true;
                    return true;
                }

                faults.Add(TaskbarDiscoveryFault.HostExclusionUntrusted);
            }

            if (!TryReadClassName(windowHandle, out string className))
            {
                if (TaskbarNativeMethods.IsWindow(windowHandle))
                {
                    faults.Add(TaskbarDiscoveryFault.ChildClassUnavailable);
                }

                return true;
            }

            NativeObstacleKind? kind = className switch
            {
                NotificationAreaClassName => NativeObstacleKind.NotificationArea,
                ClockClassName => NativeObstacleKind.Clock,
                _ => null,
            };
            if (kind is null || !TaskbarNativeMethods.IsWindowVisible(windowHandle))
            {
                return true;
            }

            if (!TaskbarNativeMethods.GetWindowRect(windowHandle, out TaskbarNativeMethods.NativeRect nativeRect))
            {
                faults.Add(TaskbarDiscoveryFault.ChildBoundsUnavailable);
                return true;
            }

            PixelRect bounds = nativeRect.ToPixelRect();
            if (!bounds.IsValid || !taskbarBounds.Contains(bounds))
            {
                faults.Add(TaskbarDiscoveryFault.ChildBoundsUnavailable);
                return true;
            }

            obstacles.Add(new NativeTaskbarObstacle(kind.Value, bounds));
            return true;
        };

        if (!TaskbarNativeMethods.EnumChildWindows(taskbar, callback, nint.Zero))
        {
            faults.Add(TaskbarDiscoveryFault.ChildEnumerationFailed);
        }

        int notificationAreaCount = obstacles.Count(
            obstacle => obstacle.Kind == NativeObstacleKind.NotificationArea);
        if (notificationAreaCount == 0)
        {
            faults.Add(TaskbarDiscoveryFault.NotificationAreaMissing);
        }
        else if (notificationAreaCount > 1)
        {
            faults.Add(TaskbarDiscoveryFault.NotificationAreaDuplicate);
        }

        return new ChildProbe(obstacles, [.. faults], ignoredHostMatched);
    }

    private static bool IsTrustedHostExclusion(nint host, nint taskbar)
    {
        if (host == nint.Zero ||
            !TaskbarNativeMethods.IsWindow(host) ||
            TaskbarNativeMethods.GetAncestor(host, TaskbarNativeMethods.GetAncestorParent) != taskbar ||
            !TryReadClassName(host, out string className) ||
            !string.Equals(className, HostViewClassName, StringComparison.Ordinal))
        {
            return false;
        }

        uint threadId = TaskbarNativeMethods.GetWindowThreadProcessId(host, out uint processId);
        return threadId != 0 && processId == (uint)Environment.ProcessId;
    }

    private static bool TryReadClassName(nint windowHandle, out string className)
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

    private static NativeTaskbarProbe Failed(TaskbarDiscoveryFault fault) =>
        Empty([fault]);

    private static NativeTaskbarProbe Empty(IEnumerable<TaskbarDiscoveryFault> faults) =>
        new(
            nint.Zero,
            0,
            default,
            0,
            default,
            default,
            [],
            [.. faults.Distinct()],
            IgnoredHostMatched: false);

    private sealed record ChildProbe(
        IReadOnlyList<NativeTaskbarObstacle> Obstacles,
        IReadOnlyList<TaskbarDiscoveryFault> Faults,
        bool IgnoredHostMatched);
}
