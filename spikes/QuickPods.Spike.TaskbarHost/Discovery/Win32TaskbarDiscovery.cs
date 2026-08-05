using System.Runtime.InteropServices;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;

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
        PrimaryTaskbarEnumeration enumeration = EnumeratePrimaryTaskbars();
        List<TaskbarDiscoveryFault> faults = [.. enumeration.Faults];
        IReadOnlyList<nint> primaryTaskbars = enumeration.Handles;

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
        return DiscoverTarget(taskbarHandle, ignoredWindowHandle, faults);
    }

    internal static Win32TaskbarContinuityProbe DiscoverAnchored(
        TaskbarContinuityAnchor anchor,
        nint ignoredWindowHandle = default)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        TaskbarContinuityTopLevelDecision selection = ProbeContinuityRoute(anchor);
        TaskbarContinuityEvidence evidence = selection.Evidence with
        {
            ExistingHostSupplied = ignoredWindowHandle != nint.Zero,
        };
        if (selection.FailureReason != TaskbarContinuityFailureReason.None)
        {
            return Win32TaskbarContinuityProbe.Failed(
                evidence,
                selection.Route,
                selection.FailureReason);
        }

        if (ignoredWindowHandle == nint.Zero)
        {
            return Win32TaskbarContinuityProbe.Failed(
                evidence,
                selection.Route,
                TaskbarContinuityFailureReason.ExistingHostUnavailable);
        }

        return DiscoverAnchoredTarget(
            anchor,
            ignoredWindowHandle,
            selection.Route,
            evidence);
    }

    /// <summary>
    /// Performs only the top-level Shell selection needed to decide whether an
    /// automation event may enter the bounded visible-continuity path. The
    /// caller must still run the full anchored discovery before retaining the
    /// native surface beyond that bounded probe.
    /// </summary>
    internal static TaskbarContinuityTopLevelDecision ProbeContinuityRoute(
        TaskbarContinuityAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        PrimaryTaskbarEnumeration enumeration = EnumeratePrimaryTaskbars();
        return SelectContinuityRoute(
            anchor.TaskbarHandle,
            enumeration.Handles,
            enumeration.Faults);
    }

    internal static TaskbarContinuityTopLevelDecision SelectContinuityRoute(
        nint expectedTaskbarHandle,
        IReadOnlyList<nint> enumeratedTaskbars,
        IReadOnlyList<TaskbarDiscoveryFault> enumerationFaults)
    {
        ArgumentNullException.ThrowIfNull(enumeratedTaskbars);
        ArgumentNullException.ThrowIfNull(enumerationFaults);
        bool enumerationSucceeded = !enumerationFaults.Any(
            static fault => fault.Code == TaskbarDiscoveryFaultCode.TopLevelEnumerationFailed);
        bool classReadsSucceeded = !enumerationFaults.Any(
            static fault => fault.Code == TaskbarDiscoveryFaultCode.TopLevelClassReadFailed);
        bool expectedWasEnumerated = enumeratedTaskbars.Count == 1 &&
            enumeratedTaskbars[0] == expectedTaskbarHandle;
        bool noCompetingTaskbar =
            enumeratedTaskbars.Count == 0 || expectedWasEnumerated;
        var evidence = new TaskbarContinuityEvidence
        {
            TopLevelEnumerationSucceeded = enumerationSucceeded,
            TopLevelClassReadsSucceeded = classReadsSucceeded,
            NoCompetingPrimaryTaskbar = noCompetingTaskbar,
            ExpectedHandleSelected =
                expectedTaskbarHandle != nint.Zero &&
                noCompetingTaskbar &&
                enumerationSucceeded &&
                classReadsSucceeded,
            ExpectedHandleWasEnumerated = expectedWasEnumerated,
        };

        if (!enumerationSucceeded)
        {
            return new(
                TaskbarContinuityRoute.None,
                TaskbarContinuityFailureReason.TopLevelEnumerationFailed,
                evidence);
        }

        if (!classReadsSucceeded)
        {
            return new(
                TaskbarContinuityRoute.None,
                TaskbarContinuityFailureReason.TopLevelClassReadFailed,
                evidence);
        }

        if (enumeratedTaskbars.Count > 1)
        {
            return new(
                TaskbarContinuityRoute.None,
                TaskbarContinuityFailureReason.PrimaryTaskbarDuplicate,
                evidence);
        }

        if (enumeratedTaskbars.Count == 1 && !expectedWasEnumerated)
        {
            return new(
                TaskbarContinuityRoute.None,
                TaskbarContinuityFailureReason.ExpectedTaskbarChanged,
                evidence);
        }

        if (expectedTaskbarHandle == nint.Zero)
        {
            return new(
                TaskbarContinuityRoute.None,
                TaskbarContinuityFailureReason.ExpectedWindowUnavailable,
                evidence);
        }

        return new(
            expectedWasEnumerated
                ? TaskbarContinuityRoute.EnumeratedExpected
                : TaskbarContinuityRoute.DirectExpected,
            TaskbarContinuityFailureReason.None,
            evidence);
    }

    private static Win32TaskbarProbe DiscoverTarget(
        nint taskbarHandle,
        nint ignoredWindowHandle,
        List<TaskbarDiscoveryFault> faults)
    {
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

    private static Win32TaskbarContinuityProbe DiscoverAnchoredTarget(
        TaskbarContinuityAnchor anchor,
        nint ignoredWindowHandle,
        TaskbarContinuityRoute route,
        TaskbarContinuityEvidence selectionEvidence)
    {
        TaskbarContinuityEvidence evidence = ReadAnchoredIdentityEvidence(
            anchor,
            selectionEvidence);
        TaskbarContinuityFailureReason identityFailure =
            TaskbarContinuityEvidencePolicy.GetFailureReason(
                evidence,
                requireNativeChildren: false,
                requireAutomation: false);
        if (identityFailure != TaskbarContinuityFailureReason.None)
        {
            return Win32TaskbarContinuityProbe.Failed(evidence, route, identityFailure);
        }

        List<TaskbarDiscoveryFault> childFaults = [];
        Win32CriticalChildrenProbe criticalChildren = DiscoverCriticalChildren(
            anchor.TaskbarHandle,
            anchor.TaskbarBounds,
            ignoredWindowHandle,
            childFaults);
        bool retainedNotificationAreaContinuity =
            CanRetainNotificationAreaContinuity(route, childFaults);
        bool directHostAttachmentVerified =
            !criticalChildren.IgnoredHostMatch &&
            route == TaskbarContinuityRoute.DirectExpected &&
            TryVerifyDirectHostAttachment(
                anchor.TaskbarHandle,
                anchor.TaskbarBounds,
                anchor.Dpi,
                ignoredWindowHandle);
        evidence = evidence with
        {
            NativeChildrenComplete = childFaults.Count == 0,
            RetainedNotificationAreaContinuity = retainedNotificationAreaContinuity,
            IgnoredHostMatched = criticalChildren.IgnoredHostMatch,
            DirectHostAttachmentVerified = directHostAttachmentVerified,
        };
        if (childFaults.Count != 0 && !retainedNotificationAreaContinuity)
        {
            return Win32TaskbarContinuityProbe.Failed(
                evidence,
                route,
                GetNativeChildrenFailureReason(childFaults));
        }

        if (!criticalChildren.IgnoredHostMatch && !directHostAttachmentVerified)
        {
            return Win32TaskbarContinuityProbe.Failed(
                evidence,
                route,
                TaskbarContinuityFailureReason.ExistingHostNotAttached);
        }

        var target = new Win32TaskbarTarget(
            anchor.TaskbarHandle,
            anchor.ExplorerProcessId,
            anchor.TaskbarBounds,
            anchor.Dpi,
            new TaskbarMonitorSnapshot(
                anchor.MonitorBounds,
                anchor.WorkArea,
                IsPrimary: true),
            criticalChildren.Children);
        return new Win32TaskbarContinuityProbe(target, evidence, route);
    }

    internal static bool TryVerifyDirectHostAttachment(
        nint taskbarHandle,
        PixelRect taskbarBounds,
        uint taskbarDpi,
        nint hostHandle)
    {
        if (!OperatingSystem.IsWindows() ||
            taskbarHandle == nint.Zero ||
            hostHandle == nint.Zero ||
            !taskbarBounds.IsValid ||
            taskbarDpi == 0 ||
            !TaskbarNativeMethods.IsWindow(taskbarHandle) ||
            !TaskbarNativeMethods.IsWindow(hostHandle) ||
            !TaskbarNativeMethods.IsWindowVisible(hostHandle) ||
            TaskbarNativeMethods.GetAncestor(
                hostHandle,
                TaskbarNativeMethods.GetAncestorParent) != taskbarHandle ||
            !TryGetClassName(hostHandle, out string className) ||
            !string.Equals(
                className,
                NativeWindowClassRegistry.ViewClassName,
                StringComparison.Ordinal) ||
            TaskbarNativeMethods.GetDpiForWindow(hostHandle) != taskbarDpi ||
            !TaskbarNativeMethods.GetWindowRect(
                hostHandle,
                out TaskbarNativeMethods.NativeRect nativeHostBounds) ||
            !taskbarBounds.Contains(nativeHostBounds.ToPixelRect()))
        {
            return false;
        }

        uint threadId = TaskbarNativeMethods.GetWindowThreadProcessId(
            hostHandle,
            out uint processId);
        if (threadId == 0 || processId != (uint)Environment.ProcessId)
        {
            return false;
        }

        int cloakResult = TaskbarNativeMethods.DwmGetWindowAttribute(
            hostHandle,
            TaskbarNativeMethods.DwmWindowAttributeCloaked,
            out uint cloakState,
            sizeof(uint));
        return cloakResult == 0 && cloakState == 0;
    }

    internal static bool CanRetainNotificationAreaContinuity(
        TaskbarContinuityRoute route,
        IReadOnlyList<TaskbarDiscoveryFault> faults)
    {
        ArgumentNullException.ThrowIfNull(faults);
        return route == TaskbarContinuityRoute.DirectExpected &&
            faults.Count == 1 &&
            faults[0].Code == TaskbarDiscoveryFaultCode.NotificationAreaMissing;
    }

    internal static TaskbarContinuityFailureReason GetNativeChildrenFailureReason(
        IReadOnlyList<TaskbarDiscoveryFault> faults)
    {
        ArgumentNullException.ThrowIfNull(faults);
        TaskbarDiscoveryFaultCode[] priority =
        [
            TaskbarDiscoveryFaultCode.ChildEnumerationFailed,
            TaskbarDiscoveryFaultCode.ChildClassReadFailed,
            TaskbarDiscoveryFaultCode.CriticalChildBoundsUnavailable,
            TaskbarDiscoveryFaultCode.CriticalChildBoundsInvalid,
            TaskbarDiscoveryFaultCode.CriticalChildOutsideTaskbar,
            TaskbarDiscoveryFaultCode.NotificationAreaMissing,
            TaskbarDiscoveryFaultCode.NotificationAreaDuplicate,
        ];
        foreach (TaskbarDiscoveryFaultCode code in priority)
        {
            if (faults.Any(fault => fault.Code == code))
            {
                return code switch
                {
                    TaskbarDiscoveryFaultCode.ChildEnumerationFailed =>
                        TaskbarContinuityFailureReason.ChildEnumerationFailed,
                    TaskbarDiscoveryFaultCode.ChildClassReadFailed =>
                        TaskbarContinuityFailureReason.ChildClassReadFailed,
                    TaskbarDiscoveryFaultCode.CriticalChildBoundsUnavailable =>
                        TaskbarContinuityFailureReason.CriticalChildBoundsUnavailable,
                    TaskbarDiscoveryFaultCode.CriticalChildBoundsInvalid =>
                        TaskbarContinuityFailureReason.CriticalChildBoundsInvalid,
                    TaskbarDiscoveryFaultCode.CriticalChildOutsideTaskbar =>
                        TaskbarContinuityFailureReason.CriticalChildOutsideTaskbar,
                    TaskbarDiscoveryFaultCode.NotificationAreaMissing =>
                        TaskbarContinuityFailureReason.NotificationAreaMissing,
                    TaskbarDiscoveryFaultCode.NotificationAreaDuplicate =>
                        TaskbarContinuityFailureReason.NotificationAreaDuplicate,
                    _ => TaskbarContinuityFailureReason.NativeChildrenIncomplete,
                };
            }
        }

        return TaskbarContinuityFailureReason.NativeChildrenIncomplete;
    }

    private static TaskbarContinuityEvidence ReadAnchoredIdentityEvidence(
        TaskbarContinuityAnchor anchor,
        TaskbarContinuityEvidence seed)
    {
        nint taskbarHandle = anchor.TaskbarHandle;
        bool windowLive = TaskbarNativeMethods.IsWindow(taskbarHandle);
        bool classMatched =
            windowLive &&
            TryGetClassName(taskbarHandle, out string className) &&
            string.Equals(className, PrimaryTaskbarClass, StringComparison.Ordinal);
        bool rootMatched =
            windowLive &&
            TaskbarNativeMethods.GetAncestor(
                taskbarHandle,
                TaskbarNativeMethods.GetAncestorRoot) == taskbarHandle;
        bool parentVisible = windowLive && TaskbarNativeMethods.IsWindowVisible(taskbarHandle);

        bool cloakStateAvailable = false;
        bool uncloaked = false;
        if (windowLive)
        {
            int cloakResult = TaskbarNativeMethods.DwmGetWindowAttribute(
                taskbarHandle,
                TaskbarNativeMethods.DwmWindowAttributeCloaked,
                out uint cloakState,
                sizeof(uint));
            cloakStateAvailable = cloakResult == 0;
            uncloaked = cloakStateAvailable && cloakState == 0;
        }

        uint threadId = windowLive
            ? TaskbarNativeMethods.GetWindowThreadProcessId(taskbarHandle, out uint processId)
            : ReadNoProcess(out processId);
        bool processMatched =
            threadId != 0 &&
            processId != 0 &&
            processId == anchor.ExplorerProcessId;
        bool boundsMatched =
            windowLive &&
            TaskbarNativeMethods.GetWindowRect(
                taskbarHandle,
                out TaskbarNativeMethods.NativeRect nativeBounds) &&
            nativeBounds.ToPixelRect() == anchor.TaskbarBounds;
        bool dpiMatched =
            windowLive && TaskbarNativeMethods.GetDpiForWindow(taskbarHandle) == anchor.Dpi;

        nint monitorHandle = windowLive
            ? TaskbarNativeMethods.MonitorFromWindow(
                taskbarHandle,
                TaskbarNativeMethods.MonitorDefaultToNull)
            : nint.Zero;
        var nativeMonitor = new TaskbarNativeMethods.NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<TaskbarNativeMethods.NativeMonitorInfo>(),
        };
        bool monitorAvailable =
            monitorHandle != nint.Zero &&
            TaskbarNativeMethods.GetMonitorInfo(monitorHandle, ref nativeMonitor);
        bool primaryMonitorMatched =
            monitorAvailable &&
            (nativeMonitor.Flags & TaskbarNativeMethods.MonitorInfoPrimary) != 0;
        bool monitorBoundsMatched =
            monitorAvailable && nativeMonitor.Monitor.ToPixelRect() == anchor.MonitorBounds;
        bool workAreaMatched =
            monitorAvailable && nativeMonitor.WorkArea.ToPixelRect() == anchor.WorkArea;

        return seed with
        {
            ExpectedWindowLive = windowLive,
            ClassMatched = classMatched,
            RootMatched = rootMatched,
            ParentVisible = parentVisible,
            CloakStateAvailable = cloakStateAvailable,
            Uncloaked = uncloaked,
            ProcessMatched = processMatched,
            BoundsMatched = boundsMatched,
            DpiMatched = dpiMatched,
            MonitorAvailable = monitorAvailable,
            PrimaryMonitorMatched = primaryMonitorMatched,
            MonitorBoundsMatched = monitorBoundsMatched,
            WorkAreaMatched = workAreaMatched,
        };

        static uint ReadNoProcess(out uint processId)
        {
            processId = 0;
            return 0;
        }
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

    private static PrimaryTaskbarEnumeration EnumeratePrimaryTaskbars()
    {
        List<TaskbarDiscoveryFault> faults = [];
        List<nint> primaryTaskbars = [];

        bool VisitWindow(nint windowHandle, nint _)
        {
            if (!TryGetClassName(windowHandle, out string className))
            {
                faults.Add(new TaskbarDiscoveryFault(
                    TaskbarDiscoveryFaultCode.TopLevelClassReadFailed));
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
            faults.Add(new TaskbarDiscoveryFault(
                TaskbarDiscoveryFaultCode.TopLevelEnumerationFailed));
        }

        return new(primaryTaskbars, faults);
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

internal sealed record Win32TaskbarContinuityProbe(
    Win32TaskbarTarget? Target,
    TaskbarContinuityEvidence Evidence,
    TaskbarContinuityRoute Route,
    TaskbarContinuityFailureReason FailureReason)
{
    internal bool IsNativeVerified =>
        Target is not null &&
        Route != TaskbarContinuityRoute.None &&
        FailureReason == TaskbarContinuityFailureReason.None &&
        Evidence.IsNativeVerified;

    internal Win32TaskbarContinuityProbe(
        Win32TaskbarTarget target,
        TaskbarContinuityEvidence evidence,
        TaskbarContinuityRoute route)
        : this(
            target,
            evidence,
            route,
            TaskbarContinuityFailureReason.None)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!evidence.IsNativeVerified || route == TaskbarContinuityRoute.None)
        {
            throw new ArgumentException(
                "A verified native continuity probe requires complete evidence.",
                nameof(evidence));
        }

        if (evidence.RetainedNotificationAreaContinuity &&
            route != TaskbarContinuityRoute.DirectExpected)
        {
            throw new ArgumentException(
                "Retained notification-area evidence is valid only for direct continuity.",
                nameof(evidence));
        }

        if (evidence.DirectHostAttachmentVerified &&
            route != TaskbarContinuityRoute.DirectExpected)
        {
            throw new ArgumentException(
                "Direct host-attachment evidence is valid only for direct continuity.",
                nameof(evidence));
        }
    }

    internal static Win32TaskbarContinuityProbe Failed(
        TaskbarContinuityEvidence evidence,
        TaskbarContinuityRoute route,
        TaskbarContinuityFailureReason failureReason) =>
        new(null, evidence, route, failureReason);
}

internal readonly record struct TaskbarContinuityTopLevelDecision(
    TaskbarContinuityRoute Route,
    TaskbarContinuityFailureReason FailureReason,
    TaskbarContinuityEvidence Evidence);

internal sealed class PrimaryTaskbarEnumeration(
    IReadOnlyList<nint> handles,
    IReadOnlyList<TaskbarDiscoveryFault> faults)
{
    internal IReadOnlyList<nint> Handles { get; } = handles;

    internal IReadOnlyList<TaskbarDiscoveryFault> Faults { get; } = faults;

    public override string ToString() => nameof(PrimaryTaskbarEnumeration);
}
