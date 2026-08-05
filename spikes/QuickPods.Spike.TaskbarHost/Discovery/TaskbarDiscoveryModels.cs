using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Discovery;

internal enum CriticalTaskbarChildKind
{
    NotificationArea,
    Clock,
    TaskbarBand,
    TaskSwitch,
    TaskList,
    XamlIsland,
    UnknownObstacle,
}

internal sealed record CriticalTaskbarChildSnapshot(
    CriticalTaskbarChildKind Kind,
    PixelRect Bounds);

internal sealed record TaskbarMonitorSnapshot(
    PixelRect Bounds,
    PixelRect WorkArea,
    bool IsPrimary);

/// <summary>
/// A visible UI Automation button. The native handle is deliberately reduced to
/// a presence bit so diagnostics cannot persist a raw HWND.
/// </summary>
internal sealed record AutomationButtonSnapshot(
    string AutomationId,
    PixelRect Bounds,
    bool HasNativeWindowHandle);

internal enum TaskbarDiscoveryFaultCode
{
    UnsupportedPlatform,
    TopLevelEnumerationFailed,
    TopLevelClassReadFailed,
    PrimaryTaskbarMissing,
    PrimaryTaskbarDuplicate,
    PrimaryTaskbarNotVisible,
    TaskbarBoundsUnavailable,
    TaskbarBoundsInvalid,
    TaskbarDpiUnavailable,
    TaskbarProcessUnavailable,
    TaskbarMonitorUnavailable,
    TaskbarMonitorInfoUnavailable,
    TaskbarMonitorBoundsInvalid,
    TaskbarMonitorWorkAreaInvalid,
    TaskbarNotOnPrimaryMonitor,
    TaskbarOutsideMonitor,
    ChildEnumerationFailed,
    ChildClassReadFailed,
    CriticalChildBoundsUnavailable,
    CriticalChildBoundsInvalid,
    CriticalChildOutsideTaskbar,
    NotificationAreaMissing,
    NotificationAreaDuplicate,
    AutomationRootUnavailable,
    AutomationEnumerationFailed,
    AutomationPropertyUnavailable,
    AutomationButtonBoundsInvalid,
    AutomationButtonOutsideTaskbar,
    StartButtonMissing,
    StartButtonDuplicate,
    WidgetsButtonDuplicate,
    AutomationTimedOut,
    AutomationWorkerFailed,
    UnexpectedDiscoveryFailure,
}

internal sealed record TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode Code);

/// <summary>
/// A diagnostic-safe taskbar snapshot. Raw HWNDs, window titles and process IDs
/// are never part of serialized output.
/// </summary>
internal sealed class TaskbarSnapshot
{
    internal TaskbarSnapshot(
        nint taskbarHandle,
        uint explorerProcessId,
        PixelRect bounds,
        uint dpi,
        TaskbarMonitorSnapshot monitor,
        IEnumerable<CriticalTaskbarChildSnapshot> criticalChildren,
        IEnumerable<AutomationButtonSnapshot> automationButtons)
    {
        TaskbarHandle = taskbarHandle;
        ExplorerProcessId = explorerProcessId;
        Bounds = bounds;
        Dpi = dpi;
        Monitor = monitor;
        CriticalChildren = Copy(criticalChildren);
        AutomationButtons = Copy(automationButtons);
    }

    /// <summary>
    /// Ephemeral live-host state. It is intentionally unavailable to external
    /// diagnostic models and is explicitly excluded from JSON.
    /// </summary>
    [JsonIgnore]
    internal nint TaskbarHandle { get; }

    [JsonIgnore]
    internal uint ExplorerProcessId { get; }

    public PixelRect Bounds { get; }

    public uint Dpi { get; }

    public TaskbarMonitorSnapshot Monitor { get; }

    public IReadOnlyList<CriticalTaskbarChildSnapshot> CriticalChildren { get; }

    [JsonIgnore]
    public IReadOnlyList<AutomationButtonSnapshot> AutomationButtons { get; }

    private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>([.. values]);
    }
}

/// <summary>
/// Result of one read-only taskbar scan. Consumers must not use a snapshot for
/// placement unless <see cref="IsComplete"/> is true.
/// </summary>
internal sealed class TaskbarDiscoveryResult
{
    internal TaskbarDiscoveryResult(
        TaskbarSnapshot? snapshot,
        IEnumerable<TaskbarDiscoveryFault> faults)
    {
        Snapshot = snapshot;
        Faults = new ReadOnlyCollection<TaskbarDiscoveryFault>([.. faults.Distinct()]);
    }

    public bool IsComplete => Snapshot is not null && Faults.Count == 0;

    public TaskbarSnapshot? Snapshot { get; }

    public IReadOnlyList<TaskbarDiscoveryFault> Faults { get; }
}
