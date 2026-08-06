using QuickPods.TaskbarHost.Geometry;

namespace QuickPods.TaskbarHost.Discovery;

internal enum TaskbarDiscoveryFault
{
    UnsupportedPlatform,
    TopLevelEnumerationFailed,
    TopLevelClassUnavailable,
    PrimaryTaskbarMissing,
    PrimaryTaskbarDuplicate,
    PrimaryTaskbarInvalid,
    PrimaryTaskbarHidden,
    PrimaryTaskbarCloaked,
    TaskbarBoundsUnavailable,
    TaskbarDpiUnavailable,
    TaskbarProcessUnavailable,
    TaskbarMonitorUnavailable,
    TaskbarNotOnPrimaryMonitor,
    ChildEnumerationFailed,
    ChildClassUnavailable,
    ChildBoundsUnavailable,
    NotificationAreaMissing,
    NotificationAreaDuplicate,
    HostExclusionUntrusted,
    AutomationRootUnavailable,
    AutomationEnumerationFailed,
    AutomationPropertyUnavailable,
    AutomationBoundsInvalid,
    AutomationButtonOutsideTaskbar,
    StartButtonMissing,
    StartButtonDuplicate,
    WidgetsButtonDuplicate,
    AutomationTimedOut,
    AutomationWorkerFailed,
}

internal enum NativeObstacleKind
{
    NotificationArea,
    Clock,
}

internal sealed record NativeTaskbarObstacle(NativeObstacleKind Kind, PixelRect Bounds);

internal sealed record AutomationButtonSnapshot(string AutomationId, PixelRect Bounds);

internal sealed class LiveTaskbarSnapshot
{
    public LiveTaskbarSnapshot(
        nint taskbarHandle,
        uint explorerProcessId,
        PixelRect bounds,
        uint dpi,
        PixelRect monitorBounds,
        PixelRect workArea,
        IReadOnlyList<NativeTaskbarObstacle> nativeObstacles,
        IReadOnlyList<AutomationButtonSnapshot> automationButtons)
    {
        TaskbarHandle = taskbarHandle;
        ExplorerProcessId = explorerProcessId;
        Bounds = bounds;
        Dpi = dpi;
        MonitorBounds = monitorBounds;
        WorkArea = workArea;
        NativeObstacles = nativeObstacles;
        AutomationButtons = automationButtons;
    }

    public nint TaskbarHandle { get; }

    public uint ExplorerProcessId { get; }

    public PixelRect Bounds { get; }

    public uint Dpi { get; }

    public PixelRect MonitorBounds { get; }

    public PixelRect WorkArea { get; }

    public IReadOnlyList<NativeTaskbarObstacle> NativeObstacles { get; }

    public IReadOnlyList<AutomationButtonSnapshot> AutomationButtons { get; }
}

internal sealed record TaskbarDiscoveryResult(
    LiveTaskbarSnapshot? Snapshot,
    IReadOnlyList<TaskbarDiscoveryFault> Faults,
    bool IgnoredHostMatched)
{
    public bool IsComplete => Snapshot is not null && Faults.Count == 0;
}

internal sealed record NativeTaskbarProbe(
    nint TaskbarHandle,
    uint ExplorerProcessId,
    PixelRect Bounds,
    uint Dpi,
    PixelRect MonitorBounds,
    PixelRect WorkArea,
    IReadOnlyList<NativeTaskbarObstacle> Obstacles,
    IReadOnlyList<TaskbarDiscoveryFault> Faults,
    bool IgnoredHostMatched)
{
    public bool HasTarget => TaskbarHandle != nint.Zero && Bounds.IsValid && Dpi != 0;
}

internal sealed record AutomationTaskbarProbe(
    IReadOnlyList<AutomationButtonSnapshot> Buttons,
    IReadOnlyList<TaskbarDiscoveryFault> Faults);
