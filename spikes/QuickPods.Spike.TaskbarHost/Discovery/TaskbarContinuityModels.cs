using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Discovery;

/// <summary>
/// Proof that a normal, complete discovery previously identified one primary
/// taskbar. The private constructor prevents the continuity-only discovery path
/// from being entered with an arbitrary HWND during initial placement.
/// </summary>
internal sealed class TaskbarContinuityAnchor
{
    private const string StartButtonAutomationId = "StartButton";

    private TaskbarContinuityAnchor(
        TaskbarSnapshot snapshot,
        AutomationButtonSnapshot retainedStartButton)
    {
        TaskbarHandle = snapshot.TaskbarHandle;
        ExplorerProcessId = snapshot.ExplorerProcessId;
        TaskbarBounds = snapshot.Bounds;
        Dpi = snapshot.Dpi;
        MonitorBounds = snapshot.Monitor.Bounds;
        WorkArea = snapshot.Monitor.WorkArea;
        RetainedStartButton = retainedStartButton;
    }

    internal nint TaskbarHandle { get; }

    internal uint ExplorerProcessId { get; }

    internal PixelRect TaskbarBounds { get; }

    internal uint Dpi { get; }

    internal PixelRect MonitorBounds { get; }

    internal PixelRect WorkArea { get; }

    /// <summary>
    /// The unique Start landmark from the complete observation that minted this
    /// anchor. It is in-memory only and can be reused solely by the strict
    /// DirectExpected continuity path when fresh UIA reports only Start missing.
    /// </summary>
    internal AutomationButtonSnapshot RetainedStartButton { get; }

    internal static TaskbarContinuityAnchor FromCompleteDiscovery(
        TaskbarDiscoveryResult completeDiscovery)
    {
        ArgumentNullException.ThrowIfNull(completeDiscovery);
        TaskbarSnapshot? snapshot = completeDiscovery.Snapshot;
        AutomationButtonSnapshot[] startButtons = snapshot?.AutomationButtons
            .Where(static button => string.Equals(
                button.AutomationId,
                StartButtonAutomationId,
                StringComparison.Ordinal))
            .ToArray() ?? [];
        if (!completeDiscovery.IsComplete ||
            snapshot is null ||
            snapshot.TaskbarHandle == nint.Zero ||
            snapshot.ExplorerProcessId == 0 ||
            !snapshot.Bounds.IsValid ||
            snapshot.Dpi == 0 ||
            !snapshot.Monitor.IsPrimary ||
            !snapshot.Monitor.Bounds.IsValid ||
            !snapshot.Monitor.WorkArea.IsValid ||
            !snapshot.Monitor.Bounds.Contains(snapshot.Monitor.WorkArea) ||
            !snapshot.Bounds.Intersects(snapshot.Monitor.Bounds) ||
            startButtons.Length != 1 ||
            !startButtons[0].Bounds.IsValid ||
            !startButtons[0].Bounds.Intersects(snapshot.Bounds))
        {
            throw new ArgumentException(
                "A continuity anchor requires a complete, internally valid primary-taskbar discovery.",
                nameof(completeDiscovery));
        }

        return new TaskbarContinuityAnchor(snapshot, startButtons[0]);
    }

    /// <summary>
    /// Advances only the retained UIA landmark after a fresh, fault-free UIA
    /// observation has re-proved the same anchored taskbar generation. A
    /// retained/partial automation observation must never call this method.
    /// </summary>
    internal bool TryWithFreshCompleteAutomation(
        TaskbarDiscoveryResult completeDiscovery,
        out TaskbarContinuityAnchor updatedAnchor)
    {
        ArgumentNullException.ThrowIfNull(completeDiscovery);
        updatedAnchor = this;
        TaskbarSnapshot? snapshot = completeDiscovery.Snapshot;
        AutomationButtonSnapshot[] startButtons = snapshot?.AutomationButtons
            .Where(static button => string.Equals(
                button.AutomationId,
                StartButtonAutomationId,
                StringComparison.Ordinal))
            .ToArray() ?? [];
        if (!completeDiscovery.IsComplete ||
            snapshot is null ||
            snapshot.TaskbarHandle != TaskbarHandle ||
            snapshot.ExplorerProcessId != ExplorerProcessId ||
            snapshot.Bounds != TaskbarBounds ||
            snapshot.Dpi != Dpi ||
            !snapshot.Monitor.IsPrimary ||
            snapshot.Monitor.Bounds != MonitorBounds ||
            snapshot.Monitor.WorkArea != WorkArea ||
            startButtons.Length != 1 ||
            !startButtons[0].Bounds.IsValid ||
            !startButtons[0].Bounds.Intersects(snapshot.Bounds))
        {
            return false;
        }

        updatedAnchor = new TaskbarContinuityAnchor(snapshot, startButtons[0]);
        return true;
    }

    public override string ToString() => nameof(TaskbarContinuityAnchor);
}

internal enum TaskbarContinuityRoute
{
    None,
    EnumeratedExpected,
    DirectExpected,
}

internal enum TaskbarAutomationContinuityOrigin
{
    None,
    FreshComplete,
    RetainedStartFromCompleteAnchor,
}

/// <summary>
/// In-memory proof that one strict DirectExpected scan reused the unique Start
/// landmark from a previously complete observation of the same Explorer
/// generation. Only the validated factory can mint this proof.
/// </summary>
internal sealed class RetainedStartButtonContinuityProof
{
    private RetainedStartButtonContinuityProof(
        TaskbarContinuityAnchor anchor,
        IReadOnlyList<AutomationButtonSnapshot> buttons)
    {
        TaskbarHandle = anchor.TaskbarHandle;
        ExplorerProcessId = anchor.ExplorerProcessId;
        TaskbarBounds = anchor.TaskbarBounds;
        Dpi = anchor.Dpi;
        MonitorBounds = anchor.MonitorBounds;
        WorkArea = anchor.WorkArea;
        Buttons = Array.AsReadOnly([.. buttons]);
    }

    private nint TaskbarHandle { get; }

    private uint ExplorerProcessId { get; }

    private PixelRect TaskbarBounds { get; }

    private uint Dpi { get; }

    private PixelRect MonitorBounds { get; }

    private PixelRect WorkArea { get; }

    internal IReadOnlyList<AutomationButtonSnapshot> Buttons { get; }

    internal static bool TryCreate(
        TaskbarContinuityRoute route,
        PixelRect taskbarBounds,
        AutomationTaskbarProbe automation,
        TaskbarContinuityAnchor anchor,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
        out RetainedStartButtonContinuityProof? proof)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(anchor);
        proof = null;
        bool exactStartMissing =
            automation.Faults.Count == 1 &&
            automation.Faults[0].Code == TaskbarDiscoveryFaultCode.StartButtonMissing;
        bool freshButtonsValid = automation.Buttons.All(button =>
            button.Bounds.IsValid && button.Bounds.Intersects(taskbarBounds));
        bool freshStartAbsent = !automation.Buttons.Any(button =>
            string.Equals(
                button.AutomationId,
                "StartButton",
                StringComparison.Ordinal));
        if (route != TaskbarContinuityRoute.DirectExpected ||
            !taskbarBounds.IsValid ||
            taskbarBounds != anchor.TaskbarBounds ||
            !exactStartMissing ||
            !freshButtonsValid ||
            !freshStartAbsent)
        {
            return false;
        }

        proof = new RetainedStartButtonContinuityProof(
            anchor,
            [.. automation.Buttons, anchor.RetainedStartButton]);
        return true;
    }

    internal bool Matches(TaskbarDiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        TaskbarSnapshot? snapshot = discovery.Snapshot;
        return discovery.IsComplete &&
            snapshot is not null &&
            snapshot.TaskbarHandle == TaskbarHandle &&
            snapshot.ExplorerProcessId == ExplorerProcessId &&
            snapshot.Bounds == TaskbarBounds &&
            snapshot.Dpi == Dpi &&
            snapshot.Monitor.IsPrimary &&
            snapshot.Monitor.Bounds == MonitorBounds &&
            snapshot.Monitor.WorkArea == WorkArea &&
            snapshot.AutomationButtons.SequenceEqual(Buttons);
    }

    public override string ToString() => nameof(RetainedStartButtonContinuityProof);
}

internal enum TaskbarContinuityFailureReason
{
    None,
    UnsupportedPlatform,
    TopLevelEnumerationFailed,
    TopLevelClassReadFailed,
    ExpectedTaskbarChanged,
    PrimaryTaskbarDuplicate,
    ExistingHostUnavailable,
    ExpectedWindowUnavailable,
    ClassMismatch,
    RootMismatch,
    NotVisible,
    CloakStateUnavailable,
    Cloaked,
    ProcessMismatch,
    BoundsMismatch,
    DpiMismatch,
    MonitorUnavailable,
    NotPrimaryMonitor,
    MonitorBoundsMismatch,
    WorkAreaMismatch,
    NativeChildrenIncomplete,
    ChildEnumerationFailed,
    ChildClassReadFailed,
    CriticalChildBoundsUnavailable,
    CriticalChildBoundsInvalid,
    CriticalChildOutsideTaskbar,
    NotificationAreaMissing,
    NotificationAreaDuplicate,
    ExistingHostNotAttached,
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
    AutomationIncomplete,
    AutomationProvenanceInvalid,
    InvalidatedDuringScan,
    TimedOut,
    UnexpectedFailure,
}

/// <summary>
/// Diagnostic-safe continuity evidence. It intentionally contains only
/// booleans and one bounded provenance enum; raw HWNDs, process identifiers
/// and coordinates remain in the in-memory anchor and proof.
/// </summary>
internal readonly record struct TaskbarContinuityEvidence
{
    internal bool TopLevelEnumerationSucceeded { get; init; }

    internal bool TopLevelClassReadsSucceeded { get; init; }

    internal bool NoCompetingPrimaryTaskbar { get; init; }

    internal bool ExpectedHandleSelected { get; init; }

    internal bool ExpectedHandleWasEnumerated { get; init; }

    internal bool ExistingHostSupplied { get; init; }

    internal bool ExpectedWindowLive { get; init; }

    internal bool ClassMatched { get; init; }

    internal bool RootMatched { get; init; }

    internal bool ParentVisible { get; init; }

    internal bool CloakStateAvailable { get; init; }

    internal bool Uncloaked { get; init; }

    internal bool ProcessMatched { get; init; }

    internal bool BoundsMatched { get; init; }

    internal bool DpiMatched { get; init; }

    internal bool MonitorAvailable { get; init; }

    internal bool PrimaryMonitorMatched { get; init; }

    internal bool MonitorBoundsMatched { get; init; }

    internal bool WorkAreaMatched { get; init; }

    internal bool NativeChildrenComplete { get; init; }

    internal bool RetainedNotificationAreaContinuity { get; init; }

    internal bool AutomationComplete { get; init; }

    internal TaskbarAutomationContinuityOrigin AutomationOrigin { get; init; }

    internal bool IgnoredHostMatched { get; init; }

    internal bool DirectHostAttachmentVerified { get; init; }

    internal bool IsNativeVerified =>
        TaskbarContinuityEvidencePolicy.GetFailureReason(
            this,
            requireNativeChildren: true,
            requireAutomation: false) ==
        TaskbarContinuityFailureReason.None;

    internal bool IsVerified =>
        TaskbarContinuityEvidencePolicy.GetFailureReason(
            this,
            requireNativeChildren: true,
            requireAutomation: true) ==
        TaskbarContinuityFailureReason.None;
}

internal static class TaskbarContinuityEvidencePolicy
{
    internal static TaskbarContinuityFailureReason GetFailureReason(
        TaskbarContinuityEvidence evidence,
        bool requireNativeChildren,
        bool requireAutomation)
    {
        if (!evidence.TopLevelEnumerationSucceeded)
        {
            return TaskbarContinuityFailureReason.TopLevelEnumerationFailed;
        }

        if (!evidence.TopLevelClassReadsSucceeded)
        {
            return TaskbarContinuityFailureReason.TopLevelClassReadFailed;
        }

        if (!evidence.NoCompetingPrimaryTaskbar || !evidence.ExpectedHandleSelected)
        {
            return TaskbarContinuityFailureReason.ExpectedTaskbarChanged;
        }

        if (!evidence.ExistingHostSupplied)
        {
            return TaskbarContinuityFailureReason.ExistingHostUnavailable;
        }

        if (!evidence.ExpectedWindowLive)
        {
            return TaskbarContinuityFailureReason.ExpectedWindowUnavailable;
        }

        if (!evidence.ClassMatched)
        {
            return TaskbarContinuityFailureReason.ClassMismatch;
        }

        if (!evidence.RootMatched)
        {
            return TaskbarContinuityFailureReason.RootMismatch;
        }

        if (!evidence.ParentVisible)
        {
            return TaskbarContinuityFailureReason.NotVisible;
        }

        if (!evidence.CloakStateAvailable)
        {
            return TaskbarContinuityFailureReason.CloakStateUnavailable;
        }

        if (!evidence.Uncloaked)
        {
            return TaskbarContinuityFailureReason.Cloaked;
        }

        if (!evidence.ProcessMatched)
        {
            return TaskbarContinuityFailureReason.ProcessMismatch;
        }

        if (!evidence.BoundsMatched)
        {
            return TaskbarContinuityFailureReason.BoundsMismatch;
        }

        if (!evidence.DpiMatched)
        {
            return TaskbarContinuityFailureReason.DpiMismatch;
        }

        if (!evidence.MonitorAvailable)
        {
            return TaskbarContinuityFailureReason.MonitorUnavailable;
        }

        if (!evidence.PrimaryMonitorMatched)
        {
            return TaskbarContinuityFailureReason.NotPrimaryMonitor;
        }

        if (!evidence.MonitorBoundsMatched)
        {
            return TaskbarContinuityFailureReason.MonitorBoundsMismatch;
        }

        if (!evidence.WorkAreaMatched)
        {
            return TaskbarContinuityFailureReason.WorkAreaMismatch;
        }

        if (requireNativeChildren &&
            !evidence.NativeChildrenComplete &&
            !evidence.RetainedNotificationAreaContinuity)
        {
            return TaskbarContinuityFailureReason.NativeChildrenIncomplete;
        }

        if (requireNativeChildren &&
            !evidence.IgnoredHostMatched &&
            !evidence.DirectHostAttachmentVerified)
        {
            return TaskbarContinuityFailureReason.ExistingHostNotAttached;
        }

        if (!requireAutomation)
        {
            return TaskbarContinuityFailureReason.None;
        }

        if (evidence.AutomationComplete)
        {
            return evidence.AutomationOrigin == TaskbarAutomationContinuityOrigin.FreshComplete
                ? TaskbarContinuityFailureReason.None
                : TaskbarContinuityFailureReason.AutomationProvenanceInvalid;
        }

        return evidence.AutomationOrigin switch
        {
            TaskbarAutomationContinuityOrigin.RetainedStartFromCompleteAnchor =>
                TaskbarContinuityFailureReason.None,
            TaskbarAutomationContinuityOrigin.None =>
                TaskbarContinuityFailureReason.AutomationIncomplete,
            _ => TaskbarContinuityFailureReason.AutomationProvenanceInvalid,
        };
    }
}

internal sealed class TaskbarContinuityDiscoveryResult
{
    private TaskbarContinuityDiscoveryResult(
        TaskbarDiscoveryResult? discovery,
        TaskbarContinuityEvidence evidence,
        TaskbarContinuityRoute route,
        TaskbarContinuityFailureReason failureReason)
    {
        Discovery = discovery;
        Evidence = evidence;
        Route = route;
        FailureReason = failureReason;
    }

    internal bool IsVerified =>
        Route != TaskbarContinuityRoute.None &&
        FailureReason == TaskbarContinuityFailureReason.None &&
        Evidence.IsVerified &&
        Discovery?.IsComplete == true;

    internal TaskbarDiscoveryResult? Discovery { get; }

    internal TaskbarContinuityEvidence Evidence { get; }

    internal TaskbarContinuityRoute Route { get; }

    internal TaskbarContinuityFailureReason FailureReason { get; }

    internal static TaskbarContinuityDiscoveryResult Verified(
        TaskbarDiscoveryResult discovery,
        TaskbarContinuityEvidence evidence,
        TaskbarContinuityRoute route,
        RetainedStartButtonContinuityProof? retainedStartProof = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        bool retainedStart = evidence.AutomationOrigin ==
            TaskbarAutomationContinuityOrigin.RetainedStartFromCompleteAnchor;
        if (!discovery.IsComplete ||
            !evidence.IsVerified ||
            route == TaskbarContinuityRoute.None ||
            (evidence.RetainedNotificationAreaContinuity &&
                route != TaskbarContinuityRoute.DirectExpected) ||
            (retainedStart &&
                route != TaskbarContinuityRoute.DirectExpected) ||
            retainedStart != (retainedStartProof is not null) ||
            (retainedStartProof is not null && !retainedStartProof.Matches(discovery)) ||
            (evidence.DirectHostAttachmentVerified &&
                route != TaskbarContinuityRoute.DirectExpected))
        {
            throw new ArgumentException(
                "A verified continuity result requires complete discovery and evidence.",
                nameof(discovery));
        }

        return new(discovery, evidence, route, TaskbarContinuityFailureReason.None);
    }

    internal static TaskbarContinuityDiscoveryResult Failed(
        TaskbarContinuityEvidence evidence,
        TaskbarContinuityRoute route,
        TaskbarContinuityFailureReason failureReason)
    {
        if (failureReason == TaskbarContinuityFailureReason.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureReason),
                "A failed continuity result requires a failure reason.");
        }

        return new(null, evidence, route, failureReason);
    }

    public override string ToString() =>
        $"{nameof(TaskbarContinuityDiscoveryResult)} " +
        $"{{ IsVerified = {IsVerified}, Route = {Route}, FailureReason = {FailureReason}, " +
        $"Evidence = {Evidence} }}";
}
