using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Discovery;

/// <summary>
/// Performs a read-only scan of the primary Windows taskbar. The service never
/// guesses: any missing, duplicate, failed or timed-out observation makes the
/// result incomplete.
/// </summary>
internal sealed class TaskbarDiscoveryService
{
    private readonly nint ignoredWindowHandle;

    internal TaskbarDiscoveryService(nint ignoredWindowHandle = default)
    {
        this.ignoredWindowHandle = ignoredWindowHandle;
    }

    /// <summary>
    /// Enumerates the primary taskbar, its critical Win32 children and visible
    /// UIA buttons. UI Automation is bounded to five seconds on a background MTA.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "This read-only process boundary must fail closed instead of crashing the host on an unexpected OS failure.")]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The instance boundary is retained as the replacement seam for watchdog-era discovery dependencies.")]
    internal async Task<TaskbarDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return new TaskbarDiscoveryResult(
                null,
                [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.UnsupportedPlatform)]);
        }

        try
        {
            return await DiscoverOnWindowsAsync(
                ignoredWindowHandle,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CreateUnexpectedFailure(ignoredHostMatch: false);
        }
    }

    /// <summary>
    /// Revalidates only a taskbar identity produced by an earlier complete
    /// normal discovery. This continuity path cannot discover an initial target
    /// and never substitutes a different Shell taskbar generation.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The continuity process boundary must return sanitized fail-closed evidence instead of terminating the host.")]
    internal async Task<TaskbarContinuityDiscoveryResult> DiscoverAnchoredAsync(
        TaskbarContinuityAnchor anchor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return TaskbarContinuityDiscoveryResult.Failed(
                default,
                TaskbarContinuityRoute.None,
                TaskbarContinuityFailureReason.UnsupportedPlatform);
        }

        try
        {
            return await DiscoverAnchoredOnWindowsAsync(
                anchor,
                ignoredWindowHandle,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return TaskbarContinuityDiscoveryResult.Failed(
                default,
                TaskbarContinuityRoute.None,
                TaskbarContinuityFailureReason.UnexpectedFailure);
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<TaskbarDiscoveryResult> DiscoverOnWindowsAsync(
        nint ignoredWindowHandle,
        CancellationToken cancellationToken)
    {
        Win32TaskbarProbe win32 = Win32TaskbarDiscovery.Discover(ignoredWindowHandle);
        try
        {
            if (win32.Target is null)
            {
                return new TaskbarDiscoveryResult(
                    null,
                    win32.Faults,
                    win32.IgnoredHostMatch);
            }

            AutomationTaskbarProbe automation = await TaskbarAutomationDiscovery.DiscoverAsync(
                win32.Target.TaskbarHandle,
                win32.Target.Bounds,
                cancellationToken).ConfigureAwait(false);

            TaskbarSnapshot snapshot = new(
                win32.Target.TaskbarHandle,
                win32.Target.ExplorerProcessId,
                win32.Target.Bounds,
                win32.Target.Dpi,
                win32.Target.Monitor,
                win32.Target.CriticalChildren,
                automation.Buttons);
            return new TaskbarDiscoveryResult(
                snapshot,
                win32.Faults.Concat(automation.Faults),
                win32.IgnoredHostMatch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CreateUnexpectedFailure(win32.IgnoredHostMatch);
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<TaskbarContinuityDiscoveryResult> DiscoverAnchoredOnWindowsAsync(
        TaskbarContinuityAnchor anchor,
        nint ignoredWindowHandle,
        CancellationToken cancellationToken)
    {
        Win32TaskbarContinuityProbe win32 = Win32TaskbarDiscovery.DiscoverAnchored(
            anchor,
            ignoredWindowHandle);
        if (!win32.IsNativeVerified || win32.Target is not Win32TaskbarTarget target)
        {
            return TaskbarContinuityDiscoveryResult.Failed(
                win32.Evidence,
                win32.Route,
                win32.FailureReason);
        }

        AutomationTaskbarProbe automation = await TaskbarAutomationDiscovery.DiscoverAsync(
            target.TaskbarHandle,
            target.Bounds,
            cancellationToken).ConfigureAwait(false);

        // UIA is an out-of-process call. Re-run the complete native probe after
        // it returns so the published child obstacles and exact-host match are
        // post-UIA evidence rather than a pre/post snapshot mixture.
        Win32TaskbarContinuityProbe finalWin32 = Win32TaskbarDiscovery.DiscoverAnchored(
            anchor,
            ignoredWindowHandle);
        TaskbarContinuityEvidence evidence = finalWin32.Evidence with
        {
            AutomationComplete = automation.Faults.Count == 0,
        };
        if (!finalWin32.IsNativeVerified)
        {
            return TaskbarContinuityDiscoveryResult.Failed(
                evidence,
                finalWin32.Route,
                finalWin32.FailureReason);
        }

        if (finalWin32.Route != win32.Route)
        {
            return TaskbarContinuityDiscoveryResult.Failed(
                evidence,
                finalWin32.Route,
                TaskbarContinuityFailureReason.ExpectedTaskbarChanged);
        }

        IReadOnlyList<AutomationButtonSnapshot> automationButtons = automation.Buttons;
        bool retainedAutomationContinuity = TryRetainStartButtonContinuity(
            finalWin32.Route,
            target.Bounds,
            automation,
            anchor.RetainedStartButton,
            out IReadOnlyList<AutomationButtonSnapshot> retainedButtons);
        if (retainedAutomationContinuity)
        {
            automationButtons = retainedButtons;
            evidence = evidence with { RetainedStartButtonContinuity = true };
        }

        TaskbarContinuityFailureReason failureReason =
            TaskbarContinuityEvidencePolicy.GetFailureReason(
                evidence,
                requireNativeChildren: true,
                requireAutomation: true);
        if (failureReason == TaskbarContinuityFailureReason.AutomationIncomplete)
        {
            failureReason = GetAutomationFailureReason(automation.Faults);
        }

        if (failureReason != TaskbarContinuityFailureReason.None)
        {
            return TaskbarContinuityDiscoveryResult.Failed(
                evidence,
                finalWin32.Route,
                failureReason);
        }

        Win32TaskbarTarget finalTarget = finalWin32.Target!;
        var snapshot = new TaskbarSnapshot(
            finalTarget.TaskbarHandle,
            finalTarget.ExplorerProcessId,
            finalTarget.Bounds,
            finalTarget.Dpi,
            finalTarget.Monitor,
            finalTarget.CriticalChildren,
            automationButtons);
        var discovery = new TaskbarDiscoveryResult(
            snapshot,
            faults: [],
            ignoredHostMatch: evidence.IgnoredHostMatched);
        return TaskbarContinuityDiscoveryResult.Verified(
            discovery,
            evidence,
            finalWin32.Route);
    }

    internal static bool TryRetainStartButtonContinuity(
        TaskbarContinuityRoute route,
        PixelRect taskbarBounds,
        AutomationTaskbarProbe automation,
        AutomationButtonSnapshot retainedStartButton,
        out IReadOnlyList<AutomationButtonSnapshot> buttons)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(retainedStartButton);
        buttons = automation.Buttons;
        bool exactStartMissing =
            automation.Faults.Count == 1 &&
            automation.Faults[0].Code == TaskbarDiscoveryFaultCode.StartButtonMissing;
        bool retainedStartValid =
            string.Equals(
                retainedStartButton.AutomationId,
                "StartButton",
                StringComparison.Ordinal) &&
            retainedStartButton.Bounds.IsValid &&
            retainedStartButton.Bounds.Intersects(taskbarBounds);
        bool freshButtonsValid = automation.Buttons.All(button =>
            button.Bounds.IsValid && button.Bounds.Intersects(taskbarBounds));
        bool freshStartAbsent = !automation.Buttons.Any(button =>
            string.Equals(
                button.AutomationId,
                "StartButton",
                StringComparison.Ordinal));
        if (route != TaskbarContinuityRoute.DirectExpected ||
            !taskbarBounds.IsValid ||
            !exactStartMissing ||
            !retainedStartValid ||
            !freshButtonsValid ||
            !freshStartAbsent)
        {
            return false;
        }

        buttons = [.. automation.Buttons, retainedStartButton];
        return true;
    }

    internal static TaskbarContinuityFailureReason GetAutomationFailureReason(
        IReadOnlyList<TaskbarDiscoveryFault> faults)
    {
        ArgumentNullException.ThrowIfNull(faults);
        foreach (TaskbarDiscoveryFault fault in faults)
        {
            TaskbarContinuityFailureReason reason = fault.Code switch
            {
                TaskbarDiscoveryFaultCode.AutomationRootUnavailable =>
                    TaskbarContinuityFailureReason.AutomationRootUnavailable,
                TaskbarDiscoveryFaultCode.AutomationEnumerationFailed =>
                    TaskbarContinuityFailureReason.AutomationEnumerationFailed,
                TaskbarDiscoveryFaultCode.AutomationPropertyUnavailable =>
                    TaskbarContinuityFailureReason.AutomationPropertyUnavailable,
                TaskbarDiscoveryFaultCode.AutomationButtonBoundsInvalid =>
                    TaskbarContinuityFailureReason.AutomationButtonBoundsInvalid,
                TaskbarDiscoveryFaultCode.AutomationButtonOutsideTaskbar =>
                    TaskbarContinuityFailureReason.AutomationButtonOutsideTaskbar,
                TaskbarDiscoveryFaultCode.StartButtonMissing =>
                    TaskbarContinuityFailureReason.StartButtonMissing,
                TaskbarDiscoveryFaultCode.StartButtonDuplicate =>
                    TaskbarContinuityFailureReason.StartButtonDuplicate,
                TaskbarDiscoveryFaultCode.WidgetsButtonDuplicate =>
                    TaskbarContinuityFailureReason.WidgetsButtonDuplicate,
                TaskbarDiscoveryFaultCode.AutomationTimedOut =>
                    TaskbarContinuityFailureReason.AutomationTimedOut,
                TaskbarDiscoveryFaultCode.AutomationWorkerFailed =>
                    TaskbarContinuityFailureReason.AutomationWorkerFailed,
                _ => TaskbarContinuityFailureReason.None,
            };
            if (reason != TaskbarContinuityFailureReason.None)
            {
                return reason;
            }
        }

        return TaskbarContinuityFailureReason.AutomationIncomplete;
    }

    internal static TaskbarDiscoveryResult CreateUnexpectedFailure(bool ignoredHostMatch) =>
        new(
            null,
            [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.UnexpectedDiscoveryFailure)],
            ignoredHostMatch);
}
