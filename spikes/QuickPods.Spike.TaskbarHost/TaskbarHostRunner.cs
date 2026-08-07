using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Automation;
using QuickPods.Spike.TaskbarHost.Diagnostics;
using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;
using QuickPods.Spike.TaskbarHost.Placement;
using QuickPods.Spike.TaskbarHost.Presentation;
using QuickPods.Spike.TaskbarHost.Runtime;

namespace QuickPods.Spike.TaskbarHost;

internal sealed class TaskbarHostRunner
{
    private static readonly TimeSpan LayoutRescanInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FallbackRescanInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan NativeContinuityRescanInterval =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan NativeContinuityScanTimeout =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan AttachmentHealthInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MessagePumpInterval = TimeSpan.FromMilliseconds(10);

    [SupportedOSPlatform("windows")]
    public static Task<bool> RunAsync(
        TaskbarHostOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Command != TaskbarCommand.Host || !options.LiveHostConfirmed)
        {
            throw new InvalidOperationException("The live host runner requires confirmed host options.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(
            () => RunThreadEntry(options, completion, cancellationToken))
        {
            IsBackground = true,
            Name = "QuickPods.TaskbarHost.WindowThread",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return completion.Task;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "No managed exception may escape the native-window thread entry point and terminate the process.")]
    [SupportedOSPlatform("windows")]
    private static void RunThreadEntry(
        TaskbarHostOptions options,
        TaskCompletionSource<bool> completion,
        CancellationToken cancellationToken)
    {
        try
        {
            completion.TrySetResult(RunOnWindowThread(options, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"The native host failed closed ({exception.GetType().Name}).");
            completion.TrySetResult(false);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool RunOnWindowThread(
        TaskbarHostOptions options,
        CancellationToken cancellationToken)
    {
        using var nativeHost = new NativeTaskbarHost();
        using var floatingHost = new NativeFloatingStripHost();
        using var scanDiagnostics = new TaskbarLiveScanDiagnosticCoalescer(
            Console.Error.WriteLine);
        var automationSignal = new TaskbarAutomationInvalidationSignal();
        var nativeCreationGuard = new SessionNativeCreationFailureGuard();
        long sessionStartTimestamp = Stopwatch.GetTimestamp();
        var promotionGate = new NativePromotionGate(TimeSpan.Zero);
        NativeParentStyleMode styleMode = options.Style switch
        {
            RequestedHostStyle.Child => NativeParentStyleMode.Child,
            RequestedHostStyle.Popup => NativeParentStyleMode.PopupPreserved,
            _ => throw new InvalidOperationException("Unknown native host style."),
        };

        TaskbarPresentationState state = TaskbarPresentationState.Starting;
        TaskbarAutomationWatcher? automationWatcher = null;
        bool watcherDisabledForSession = false;
        long observedAutomationGeneration = automationSignal.Generation;
        long invalidationGeneration = 0;
        bool nativeLayoutInvalidated = false;
        bool nativeAttachmentUnavailable = false;
        bool automationLayoutInvalidated = false;
        bool floatingLayoutInvalidated = false;
        bool nativeContinuityProbeRequested = false;
        NativeLayoutInvalidationReason? nativeInvalidationReason = null;
        TaskbarAutomationInvalidation? automationInvalidation = null;
        TaskbarContinuityRoute lastContinuityRoute = TaskbarContinuityRoute.None;
        VerifiedLiveLayout? currentNativeLayout = null;
        VerifiedFloatingContext? retainedFloatingContext = null;
        VerifiedFloatingContext? currentFloatingContext = null;
        PixelRect? priorNativeBounds = null;
        PixelRect activeSurfaceBounds = default;
        double sampleVolume = 0.42;
        FloatingPlacementUnavailableReason? lastFloatingPlacementFailure = null;
        Type? lastFloatingHostFailureType = null;
        long nextScanTimestamp = sessionStartTimestamp;
        long nextAttachmentCheckTimestamp = sessionStartTimestamp;

        nativeHost.LayoutInvalidated += reason =>
        {
            nativeLayoutInvalidated = true;
            invalidationGeneration = unchecked(invalidationGeneration + 1);
            nativeInvalidationReason = reason;
            if (RequiresFreshMonitorGeometry(reason))
            {
                retainedFloatingContext = null;
            }
        };
        floatingHost.LayoutInvalidated += reason =>
        {
            floatingLayoutInvalidated = true;
            invalidationGeneration = unchecked(invalidationGeneration + 1);
            nativeInvalidationReason = reason;
            if (RequiresFreshMonitorGeometry(reason))
            {
                retainedFloatingContext = null;
            }
        };
        nativeHost.Interaction += interaction =>
            ApplySampleInteraction(TaskbarPresentationSurface.Native, interaction);
        floatingHost.Interaction += interaction =>
            ApplySampleInteraction(TaskbarPresentationSurface.Floating, interaction);

        try
        {
            LiveLayoutScan initialScan = DiscoverLayout(cancellationToken);
            scanDiagnostics.Record(TaskbarLiveScanStage.Initial, initialScan.Signature);
            RetainFreshFloatingContext(initialScan, invalidatedDuringScan: false);
            if (Elapsed() >= options.Duration)
            {
                return true;
            }

            bool nativeStarted =
                initialScan.Layout is VerifiedLiveLayout initialLayout &&
                TryArmAndCreateInitialNative(initialLayout);
            if (nativeStarted)
            {
                SetState(
                    TaskbarPresentationState.NativeVisible,
                    PresentationTransitionReason.InitialNative);
                ResetInvalidationFlags();
                nextScanTimestamp = AddDuration(
                    Stopwatch.GetTimestamp(),
                    LayoutRescanInterval);
                nextAttachmentCheckTimestamp = AddDuration(
                    Stopwatch.GetTimestamp(),
                    AttachmentHealthInterval);
            }
            else
            {
                if (Elapsed() >= options.Duration)
                {
                    return true;
                }

                EnterFallback(
                    initialScan.Placement.Decision,
                    PresentationTransitionReason.InitialFallback);
            }

            while (Elapsed() < options.Duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PumpBothHosts();
                _ = ConsumeAutomationInvalidation();

                long now = Stopwatch.GetTimestamp();
                if (state == TaskbarPresentationState.NativeVisible &&
                    now >= nextAttachmentCheckTimestamp)
                {
                    nextAttachmentCheckTimestamp = AddDuration(
                        now,
                        AttachmentHealthInterval);
                    if (!nativeHost.IsCurrentContinuityStable())
                    {
                        nativeAttachmentUnavailable = true;
                        nativeLayoutInvalidated = true;
                    }
                }

                if (state == TaskbarPresentationState.NativeVisible &&
                    (nativeLayoutInvalidated || automationLayoutInvalidated))
                {
                    WriteInvalidationSummary();
                    EnterFallback(
                        PlacementDecision.TransientUnknown,
                        nativeAttachmentUnavailable
                            ? PresentationTransitionReason.NativeAttachmentUnavailable
                            : PresentationTransitionReason.NativeInvalidated);
                    continue;
                }

                if (state == TaskbarPresentationState.NativeVisible &&
                    nativeContinuityProbeRequested)
                {
                    RunNativeWatchdogScan(continuityTriggeredByAutomation: true);
                    continue;
                }

                if (floatingLayoutInvalidated)
                {
                    PresentationTransitionReason reason =
                        PresentationTransitionReason.FloatingInvalidated;
                    DestroyFloatingHost();
                    StopWatcher();
                    ResetInvalidationFlags();
                    SetFallbackSurface(reason);
                    promotionGate.BeginModeSwitch(Elapsed());
                    nextScanTimestamp = Stopwatch.GetTimestamp();
                    continue;
                }

                if ((state is TaskbarPresentationState.FloatingFallback or
                        TaskbarPresentationState.HiddenFallback) &&
                    automationLayoutInvalidated)
                {
                    StopWatcher();
                    promotionGate.BeginModeSwitch(Elapsed());
                    ResetInvalidationFlags();
                    nextScanTimestamp = Stopwatch.GetTimestamp();
                    continue;
                }

                if (now >= nextScanTimestamp)
                {
                    if (state == TaskbarPresentationState.NativeVisible)
                    {
                        RunNativeWatchdogScan(continuityTriggeredByAutomation: false);
                    }
                    else
                    {
                        RunFallbackScan();
                    }
                }

                Thread.Sleep(MessagePumpInterval);
            }

            scanDiagnostics.Flush();
            return true;
        }
        catch (HostDurationElapsedException)
        {
            scanDiagnostics.Flush();
            return true;
        }
        finally
        {
            CleanupAllHosts();
        }

        bool TryArmAndCreateInitialNative(VerifiedLiveLayout initialLayout)
        {
            if (!TryStartWatcher(initialLayout.Identity.TaskbarHandle))
            {
                return false;
            }

            ResetInvalidationFlags();
            long generationBeforeScan = automationSignal.Generation;
            long invalidationBeforeScan = invalidationGeneration;
            LiveLayoutScan armedScan = DiscoverLayout(cancellationToken);
            long generationAfterScan = automationSignal.Generation;
            bool invalidatedDuringScan =
                generationBeforeScan != generationAfterScan ||
                invalidationBeforeScan != invalidationGeneration ||
                nativeLayoutInvalidated ||
                automationLayoutInvalidated ||
                floatingLayoutInvalidated;
            scanDiagnostics.Record(
                TaskbarLiveScanStage.Armed,
                armedScan.Signature.WithInvalidatedDuringScan(invalidatedDuringScan));
            RetainFreshFloatingContext(armedScan, invalidatedDuringScan);
            if (Elapsed() >= options.Duration)
            {
                StopWatcher();
                return false;
            }

            VerifiedLiveLayout? armedLayout = armedScan.Layout;
            bool stable =
                !invalidatedDuringScan &&
                armedLayout is not null &&
                armedLayout.Identity.TaskbarHandle == automationWatcher?.TaskbarHandle &&
                initialLayout.ToPromotionCandidate() == armedLayout.ToPromotionCandidate() &&
                TaskbarAutomationArmingFence.IsStable(
                    automationWatcher!.TaskbarHandle,
                    armedLayout.Identity.TaskbarHandle,
                    generationBeforeScan,
                    generationAfterScan);
            if (!stable || armedLayout is null)
            {
                StopWatcher();
                return false;
            }

            observedAutomationGeneration = generationAfterScan;
            return TryCreateNative(armedLayout, generationAfterScan);
        }

        void RunNativeWatchdogScan(bool continuityTriggeredByAutomation)
        {
            VerifiedLiveLayout? current = currentNativeLayout;
            if (current is null || !nativeHost.IsCreated)
            {
                EnterFallback(
                    PlacementDecision.TransientUnknown,
                    PresentationTransitionReason.WatchdogUnsafe);
                return;
            }

            TaskbarAutomationInvalidation? initiatingInvalidation = automationInvalidation;
            long continuityDeadline = AddDuration(
                Stopwatch.GetTimestamp(),
                NativeContinuityScanTimeout);
            int attempt = 0;
            while (true)
            {
                attempt++;
                ResetInvalidationFlags();
                var scanFence = new TaskbarContinuityScanFence(
                    automationSignal.Generation,
                    invalidationGeneration);
                TimeSpan scanBudget = RemainingUntil(continuityDeadline);
                if (scanBudget <= TimeSpan.Zero)
                {
                    Console.Error.WriteLine(
                        $"native-continuity=failed; route=None; reason=TimedOut; " +
                        $"attempts={attempt - 1}");
                    EnterFallback(
                        PlacementDecision.TransientUnknown,
                        PresentationTransitionReason.WatchdogUnsafe);
                    return;
                }

                NativeContinuityLiveScan continuityScan = DiscoverContinuityLayout(
                    current.ContinuityAnchor,
                    scanBudget,
                    cancellationToken);
                PumpBothHosts();
                _ = ConsumeAutomationInvalidation();
                initiatingInvalidation ??= automationInvalidation;
                var scanCompletion = new TaskbarContinuityScanCompletion(
                    automationSignal.Generation,
                    invalidationGeneration,
                    nativeLayoutInvalidated,
                    automationLayoutInvalidated,
                    floatingLayoutInvalidated,
                    nativeContinuityProbeRequested);
                bool invalidatedDuringScan = scanFence.IsBroken(scanCompletion);
                LiveLayoutScan? scan = continuityScan.Scan;
                if (scan is not null)
                {
                    scanDiagnostics.Record(
                        TaskbarLiveScanStage.Watchdog,
                        scan.Signature.WithInvalidatedDuringScan(invalidatedDuringScan));
                    RetainFreshFloatingContext(scan, invalidatedDuringScan);
                }

                if (Elapsed() >= options.Duration)
                {
                    return;
                }

                bool continuityScanUnstable =
                    invalidatedDuringScan ||
                    !continuityScan.Result.IsVerified ||
                    scan is null;
                if (continuityScanUnstable &&
                    TryPrepareVisibleContinuityRetry(
                        current,
                        continuityDeadline,
                        out TaskbarContinuityRoute retryRoute))
                {
                    if (attempt == 1)
                    {
                        Console.Error.WriteLine(
                            $"native-continuity=retrying; route={retryRoute}; " +
                            "fixed-deadline=True");
                    }

                    continue;
                }

                if (!continuityScan.Result.IsVerified || scan is null)
                {
                    Console.Error.WriteLine(
                        $"native-continuity=failed; route={continuityScan.Result.Route}; " +
                        $"reason={continuityScan.Result.FailureReason}; attempts={attempt}");
                    EnterFallback(
                        PlacementDecision.TransientUnknown,
                        PresentationTransitionReason.WatchdogUnsafe);
                    return;
                }

                VerifiedLiveLayout? verified = scan.Layout;
                TaskbarContinuityAttemptDecision attemptDecision =
                    TaskbarContinuityAttemptPolicy.Decide(new(
                        scanFence,
                        scanCompletion,
                        continuityScan.Result.IsVerified,
                        continuityScan.Result.Evidence,
                        current.Observation,
                        verified?.Observation,
                        current.Identity,
                        current.Bounds,
                        verified?.Identity,
                        verified?.Bounds,
                        nativeHost.IsCurrentContinuityStable()));

                if (attemptDecision.WatchdogDecision == TaskbarWatchdogDecision.KeepVisible &&
                    verified is not null)
                {
                    currentNativeLayout = current with
                    {
                        ContinuityAnchor = verified.ContinuityAnchor,
                        Observation = attemptDecision.SafetyObservation!,
                    };
                    TaskbarContinuityRoute route = continuityScan.Result.Route;
                    bool automationTriggered =
                        continuityTriggeredByAutomation || initiatingInvalidation is not null;
                    if (route != lastContinuityRoute || automationTriggered || attempt > 1)
                    {
                        string trigger =
                            initiatingInvalidation is TaskbarAutomationInvalidation invalidation
                                ? $"{invalidation.SourceClass}{invalidation.Kind}"
                                : "Watchdog";
                        Console.Error.WriteLine(
                            $"native-continuity=verified; route={route}; " +
                            $"trigger={trigger}; attempts={attempt}; current-bounds-safe=True; " +
                            $"retained-notification-area=" +
                            $"{continuityScan.Result.Evidence.RetainedNotificationAreaContinuity}; " +
                            $"automation-origin=" +
                            $"{continuityScan.Result.Evidence.AutomationOrigin}; " +
                            $"direct-host-attachment=" +
                            $"{continuityScan.Result.Evidence.DirectHostAttachmentVerified}");
                    }

                    lastContinuityRoute = route;
                    ResetInvalidationFlags();
                    TimeSpan nextInterval = route == TaskbarContinuityRoute.DirectExpected
                        ? NativeContinuityRescanInterval
                        : LayoutRescanInterval;
                    nextScanTimestamp = AddDuration(
                        Stopwatch.GetTimestamp(),
                        nextInterval);
                    return;
                }

                EnterFallback(
                    scan.Placement.Decision,
                    PresentationTransitionReason.WatchdogUnsafe);
                return;
            }
        }

        bool TryPrepareVisibleContinuityRetry(
            VerifiedLiveLayout current,
            long continuityDeadline,
            out TaskbarContinuityRoute retryRoute)
        {
            retryRoute = TaskbarContinuityRoute.None;
            if (RemainingUntil(continuityDeadline) <= TimeSpan.Zero ||
                nativeLayoutInvalidated ||
                automationLayoutInvalidated ||
                floatingLayoutInvalidated ||
                !nativeHost.IsCreated)
            {
                return false;
            }

            if (!TryVerifyVisibleNativePreflight(current, out Win32TaskbarContinuityProbe? preflight) ||
                !TaskbarNativeContinuityInvalidationPolicy.CanRetainVisibleRoute(
                    preflight!.Route,
                    lastContinuityRoute) ||
                RemainingUntil(continuityDeadline) <= TimeSpan.Zero)
            {
                return false;
            }

            retryRoute = preflight.Route;
            return true;
        }

        bool TryVerifyVisibleNativePreflight(
            VerifiedLiveLayout current,
            out Win32TaskbarContinuityProbe? preflight)
        {
            preflight = null;
            long expectedGeneration = observedAutomationGeneration;
            if (!nativeHost.IsCreated ||
                automationSignal.Generation != expectedGeneration ||
                !nativeHost.IsCurrentContinuityStable())
            {
                return false;
            }

            preflight = Win32TaskbarDiscovery.DiscoverAnchored(
                current.ContinuityAnchor,
                nativeHost.WindowHandle);
            TaskbarLayoutObservation? nativeObservation =
                preflight.Target is Win32TaskbarTarget target
                    ? TaskbarLayoutAdapter.CreateNativePreflightObservation(
                        current.Observation,
                        target.CriticalChildren)
                    : null;
            bool currentBoundsNativeSafe =
                preflight.IsNativeVerified &&
                SafeRegionCalculator.IsExistingPlacementSafe(
                    nativeObservation,
                    TaskbarPlacementOptions.Default,
                    current.Bounds);
            return currentBoundsNativeSafe &&
                automationSignal.Generation == expectedGeneration &&
                nativeHost.IsCurrentContinuityStable();
        }

        void RunFallbackScan()
        {
            ResetInvalidationFlags();
            long generationBeforeScan = automationSignal.Generation;
            long invalidationBeforeScan = invalidationGeneration;
            LiveLayoutScan scan = DiscoverLayout(cancellationToken);
            long generationAfterScan = automationSignal.Generation;
            PumpBothHosts();
            _ = ConsumeAutomationInvalidation();
            bool invalidatedDuringScan =
                generationBeforeScan != generationAfterScan ||
                invalidationBeforeScan != invalidationGeneration ||
                nativeLayoutInvalidated ||
                automationLayoutInvalidated ||
                floatingLayoutInvalidated;
            scanDiagnostics.Record(
                TaskbarLiveScanStage.Recovery,
                scan.Signature.WithInvalidatedDuringScan(invalidatedDuringScan));
            RetainFreshFloatingContext(scan, invalidatedDuringScan);
            if (Elapsed() >= options.Duration)
            {
                return;
            }

            bool floatingAvailable = SetFallbackSurface(
                invalidatedDuringScan
                    ? PresentationTransitionReason.ScanInvalidated
                    : scan.Placement.Decision == PlacementDecision.VerifiedNoFit
                        ? PresentationTransitionReason.VerifiedNoFit
                        : PresentationTransitionReason.TransientUnknown);
            bool promotionReady = false;
            if (NativePipelineAvailable() &&
                scan.Layout is VerifiedLiveLayout verified &&
                !invalidatedDuringScan)
            {
                if (automationWatcher is null ||
                    automationWatcher.TaskbarHandle != verified.Identity.TaskbarHandle)
                {
                    if (TryStartWatcher(verified.Identity.TaskbarHandle))
                    {
                        promotionGate.BeginModeSwitch(Elapsed());
                    }
                }
                else
                {
                    promotionReady = promotionGate.Observe(new(
                        scan.Placement.Decision,
                        verified.ToPromotionCandidate(),
                        InvalidatedDuringScan: false,
                        LayoutInvalidated: false,
                        Elapsed()));
                }
            }
            else
            {
                promotionReady = promotionGate.Observe(new(
                    invalidatedDuringScan
                        ? PlacementDecision.TransientUnknown
                        : scan.Placement.Decision,
                    Candidate: null,
                    InvalidatedDuringScan: invalidatedDuringScan,
                    LayoutInvalidated: nativeLayoutInvalidated || floatingLayoutInvalidated,
                    Elapsed()));
                StopWatcher();
            }

            TaskbarPresentationTransition transition = TaskbarPresentationPolicy.Decide(new(
                state,
                scan.Placement.Decision,
                invalidatedDuringScan,
                NativePipelineAvailable(),
                floatingAvailable,
                promotionReady,
                RecoveryElapsed: TimeSpan.Zero));
            if (transition.DesiredSurface == TaskbarPresentationSurface.Native &&
                scan.Layout is VerifiedLiveLayout nativeCandidate &&
                automationWatcher is not null &&
                automationWatcher.TaskbarHandle == nativeCandidate.Identity.TaskbarHandle)
            {
                observedAutomationGeneration = generationAfterScan;
                if (TryCreateNative(nativeCandidate, generationAfterScan))
                {
                    SetState(
                        TaskbarPresentationState.NativeVisible,
                        PresentationTransitionReason.NativePromoted);
                    ResetInvalidationFlags();
                    nextScanTimestamp = AddDuration(
                        Stopwatch.GetTimestamp(),
                        LayoutRescanInterval);
                    nextAttachmentCheckTimestamp = AddDuration(
                        Stopwatch.GetTimestamp(),
                        AttachmentHealthInterval);
                    return;
                }

                promotionGate.BeginModeSwitch(Elapsed());
                floatingAvailable = SetFallbackSurface(
                    PresentationTransitionReason.NativeCreationFailed);
            }

            state = floatingAvailable
                ? TaskbarPresentationState.FloatingFallback
                : TaskbarPresentationState.HiddenFallback;
            ResetInvalidationFlags();
            nextScanTimestamp = AddDuration(
                Stopwatch.GetTimestamp(),
                FallbackRescanInterval);
        }

        bool TryCreateNative(VerifiedLiveLayout verified, long stableAutomationGeneration)
        {
            if (!NativePipelineAvailable() ||
                automationWatcher is null ||
                automationWatcher.TaskbarHandle != verified.Identity.TaskbarHandle ||
                automationSignal.Generation != stableAutomationGeneration ||
                Elapsed() >= options.Duration)
            {
                return false;
            }

            if (nativeHost.IsCreated)
            {
                throw new InvalidOperationException(
                    "A native presentation surface already exists during promotion.");
            }

            bool floatingWasVisible =
                state == TaskbarPresentationState.FloatingFallback &&
                floatingHost.IsCreated &&
                floatingHost.IsCurrentPlacementValid();
            if (floatingHost.IsCreated && !floatingWasVisible)
            {
                DestroyFloatingHost();
            }

            nativeHost.VolumeFraction = sampleVolume;
            NativeHostCreationSnapshot creation;
            try
            {
                creation = nativeHost.CreateHidden(
                    verified.Identity.TaskbarHandle,
                    verified.Bounds,
                    styleMode);
                automationSignal.SetIgnoredWindowHandle(nativeHost.WindowHandle);
            }
            catch (NativeLayoutInvalidatedException)
            {
                DestroyNativeHost();
                return false;
            }
            catch (ArgumentException exception) when (exception is not ArgumentOutOfRangeException)
            {
                DestroyNativeHost();
                return false;
            }
            catch (Exception exception) when (IsRecoverableNativeCreationFailure(exception))
            {
                DestroyNativeHost();
                NativeCreationFailureDisposition disposition =
                    nativeCreationGuard.RecordFailure();
                Console.Error.WriteLine(
                    $"native-creation=failed; kind={exception.GetType().Name}; " +
                    $"disposition={disposition}");
                return false;
            }

            observedAutomationGeneration = stableAutomationGeneration;
            PumpBothHosts();
            _ = ConsumeAutomationInvalidation();
            if (nativeLayoutInvalidated ||
                automationLayoutInvalidated ||
                floatingLayoutInvalidated ||
                automationSignal.Generation != stableAutomationGeneration ||
                !nativeHost.IsCurrentAttachmentValidWhileHidden() ||
                Elapsed() >= options.Duration)
            {
                DestroyNativeHost();
                return false;
            }

            if (floatingWasVisible)
            {
                floatingHost.Hide();
            }

            try
            {
                if (automationSignal.Generation != stableAutomationGeneration)
                {
                    throw new NativeLayoutInvalidatedException(
                        "The taskbar changed immediately before native promotion.");
                }

                nativeHost.Show();
            }
            catch (Exception exception) when (
                exception is NativeLayoutInvalidatedException or InvalidOperationException or Win32Exception)
            {
                DestroyNativeHost();
                RestoreFloatingAfterFailedPromotion(floatingWasVisible);
                return false;
            }

            PumpBothHosts();
            _ = ConsumeAutomationInvalidation();
            if (nativeLayoutInvalidated ||
                automationLayoutInvalidated ||
                floatingLayoutInvalidated ||
                automationSignal.Generation != stableAutomationGeneration ||
                !nativeHost.IsCurrentAttachmentValid())
            {
                DestroyNativeHost();
                RestoreFloatingAfterFailedPromotion(floatingWasVisible);
                return false;
            }

            if (floatingWasVisible)
            {
                DestroyFloatingHost();
            }

            currentNativeLayout = verified;
            priorNativeBounds = verified.Bounds;
            activeSurfaceBounds = verified.Bounds;
            nativeCreationGuard.RecordSuccessfulCreation();
            WriteCreationSummary(creation);
            return true;
        }

        void RestoreFloatingAfterFailedPromotion(bool floatingWasVisible)
        {
            if (!floatingWasVisible || !floatingHost.IsCreated)
            {
                return;
            }

            try
            {
                floatingHost.Show();
            }
            catch (Exception exception) when (IsRecoverableFloatingFailure(exception))
            {
                DestroyFloatingHost();
            }
        }

        bool TryStartWatcher(nint taskbarHandle)
        {
            if (watcherDisabledForSession)
            {
                return false;
            }

            StopWatcher();
            if (watcherDisabledForSession)
            {
                return false;
            }

            try
            {
                automationWatcher = TaskbarAutomationWatcher.Start(
                    taskbarHandle,
                    automationSignal);
                observedAutomationGeneration = automationSignal.Generation;
                return true;
            }
            catch (Exception exception) when (IsRecoverableWatcherFailure(exception))
            {
                automationWatcher = null;
                observedAutomationGeneration = automationSignal.Generation;
                watcherDisabledForSession |=
                    exception is AggregateException or TimeoutException;
                Console.Error.WriteLine(
                    $"native-watcher=failed; kind={exception.GetType().Name}; " +
                    $"session-disabled={watcherDisabledForSession}");

                return false;
            }
        }

        void EnterFallback(
            PlacementDecision decision,
            PresentationTransitionReason reason)
        {
            DestroyNativeHost();
            promotionGate.BeginModeSwitch(Elapsed());
            bool floatingAvailable = SetFallbackSurface(reason);
            StopWatcher();
            TaskbarPresentationTransition transition = TaskbarPresentationPolicy.Decide(new(
                state,
                decision,
                InvalidatedDuringScan: false,
                NativePipelineAvailable(),
                floatingAvailable,
                NativePromotionReady: false,
                RecoveryElapsed: TaskbarPresentationPolicy.MaximumNativeRecoveryDuration));
            SetState(transition.NextState, reason);
            ResetInvalidationFlags();
            nextScanTimestamp = Stopwatch.GetTimestamp();
        }

        bool SetFallbackSurface(PresentationTransitionReason reason)
        {
            if (nativeHost.IsCreated)
            {
                throw new InvalidOperationException(
                    "The native and floating presentation surfaces must never coexist.");
            }

            if (options.Fallback != RequestedFallbackMode.Floating ||
                retainedFloatingContext is not VerifiedFloatingContext context)
            {
                DestroyFloatingHost();
                SetState(TaskbarPresentationState.HiddenFallback, reason);
                return false;
            }

            FloatingPlacementResult placement = FloatingPlacementCalculator.Calculate(new(
                context.WorkArea,
                context.Dpi,
                priorNativeBounds));
            if (!placement.IsAvailable || placement.Bounds is not PixelRect bounds)
            {
                DestroyFloatingHost();
                if (lastFloatingPlacementFailure != placement.UnavailableReason)
                {
                    Console.Error.WriteLine(
                        $"floating-placement=unavailable; reason={placement.UnavailableReason}");
                }

                lastFloatingPlacementFailure = placement.UnavailableReason;
                lastFloatingHostFailureType = null;
                SetState(TaskbarPresentationState.HiddenFallback, reason);
                return false;
            }

            var desiredContext = new VerifiedFloatingContext(
                context.MonitorBounds,
                context.WorkArea,
                context.Dpi,
                bounds);
            if (floatingHost.IsCreated &&
                currentFloatingContext == desiredContext &&
                floatingHost.IsCurrentPlacementValid())
            {
                activeSurfaceBounds = bounds;
                SetState(TaskbarPresentationState.FloatingFallback, reason);
                return true;
            }

            DestroyFloatingHost();
            floatingHost.VolumeFraction = sampleVolume;
            try
            {
                _ = floatingHost.Create(bounds, context.Dpi);
                currentFloatingContext = desiredContext;
                activeSurfaceBounds = bounds;
                lastFloatingPlacementFailure = null;
                lastFloatingHostFailureType = null;
                SetState(TaskbarPresentationState.FloatingFallback, reason);
                Console.Error.WriteLine(
                    "floating-host=verified-visible; topmost=False; owned=False");
                return true;
            }
            catch (Exception exception) when (IsRecoverableFloatingFailure(exception))
            {
                DestroyFloatingHost();
                Type failureType = exception.GetType();
                if (lastFloatingHostFailureType != failureType)
                {
                    Console.Error.WriteLine(
                        $"floating-host=unavailable; kind={failureType.Name}");
                }

                lastFloatingPlacementFailure = null;
                lastFloatingHostFailureType = failureType;
                SetState(TaskbarPresentationState.HiddenFallback, reason);
                return false;
            }
        }

        LiveLayoutScan DiscoverLayout(CancellationToken token)
        {
            TimeSpan remaining = options.Duration - Elapsed();
            if (remaining <= TimeSpan.Zero)
            {
                throw new HostDurationElapsedException();
            }

            nint ignoredNativeWindow = nativeHost.IsCreated
                ? nativeHost.WindowHandle
                : nint.Zero;
            var discoveryService = new TaskbarDiscoveryService(ignoredNativeWindow);
            using var deadlineCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(token);
            deadlineCancellation.CancelAfter(remaining);
            Task<TaskbarDiscoveryResult> task = discoveryService.DiscoverAsync(
                deadlineCancellation.Token);
            try
            {
                while (!task.IsCompleted)
                {
                    deadlineCancellation.Token.ThrowIfCancellationRequested();
                    PumpBothHosts();
                    _ = ConsumeAutomationInvalidation();
                    Thread.Sleep(MessagePumpInterval);
                }

                return CreateLiveLayoutScan(task.GetAwaiter().GetResult());
            }
            catch (OperationCanceledException)
                when (!token.IsCancellationRequested && deadlineCancellation.IsCancellationRequested)
            {
                throw new HostDurationElapsedException();
            }
        }

        NativeContinuityLiveScan DiscoverContinuityLayout(
            TaskbarContinuityAnchor anchor,
            TimeSpan maximumDuration,
            CancellationToken token)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
                maximumDuration,
                TimeSpan.Zero);
            TimeSpan remaining = options.Duration - Elapsed();
            if (remaining <= TimeSpan.Zero)
            {
                throw new HostDurationElapsedException();
            }

            nint ignoredNativeWindow = nativeHost.IsCreated
                ? nativeHost.WindowHandle
                : nint.Zero;
            var discoveryService = new TaskbarDiscoveryService(ignoredNativeWindow);
            using var deadlineCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(token);
            TimeSpan timeout = remaining < maximumDuration
                ? remaining
                : maximumDuration;
            deadlineCancellation.CancelAfter(timeout);
            Task<TaskbarContinuityDiscoveryResult> task =
                discoveryService.DiscoverAnchoredAsync(
                    anchor,
                    deadlineCancellation.Token);
            bool invalidatedWhileWaiting = false;
            try
            {
                while (!task.IsCompleted)
                {
                    deadlineCancellation.Token.ThrowIfCancellationRequested();
                    PumpBothHosts();
                    _ = ConsumeAutomationInvalidation();
                    if (TaskbarContinuityAttemptPolicy.RequiresImmediateHide(new(
                            nativeInvalidationReason,
                            nativeLayoutInvalidated,
                            automationLayoutInvalidated,
                            floatingLayoutInvalidated)))
                    {
                        invalidatedWhileWaiting = true;
                        if (nativeHost.IsCreated)
                        {
                            nativeHost.Hide();
                        }

                        deadlineCancellation.Cancel();
                    }

                    Thread.Sleep(MessagePumpInterval);
                }

                TaskbarContinuityDiscoveryResult result = task.GetAwaiter().GetResult();
                TaskbarContinuityAnchor nextAnchor = anchor;
                if (result.IsVerified &&
                    result.Evidence.AutomationOrigin ==
                        TaskbarAutomationContinuityOrigin.FreshComplete &&
                    result.Discovery is TaskbarDiscoveryResult completeAutomationDiscovery &&
                    !anchor.TryWithFreshCompleteAutomation(
                        completeAutomationDiscovery,
                        out nextAnchor))
                {
                    result = TaskbarContinuityDiscoveryResult.Failed(
                        result.Evidence,
                        result.Route,
                        TaskbarContinuityFailureReason.UnexpectedFailure);
                }

                LiveLayoutScan? scan = result.IsVerified &&
                    result.Discovery is TaskbarDiscoveryResult discovery
                        ? CreateLiveLayoutScan(discovery, nextAnchor)
                        : null;
                return new(result, scan);
            }
            catch (OperationCanceledException)
                when (!token.IsCancellationRequested && deadlineCancellation.IsCancellationRequested)
            {
                if (Elapsed() >= options.Duration)
                {
                    throw new HostDurationElapsedException();
                }

                var timedOut =
                    TaskbarContinuityDiscoveryResult.Failed(
                        default,
                        TaskbarContinuityRoute.None,
                        invalidatedWhileWaiting
                            ? TaskbarContinuityFailureReason.InvalidatedDuringScan
                            : TaskbarContinuityFailureReason.TimedOut);
                return new(timedOut, Scan: null);
            }
        }

        static LiveLayoutScan CreateLiveLayoutScan(
            TaskbarDiscoveryResult discovery,
            TaskbarContinuityAnchor? continuityAnchor = null)
        {
            TaskbarLayoutObservation? observation =
                TaskbarLayoutAdapter.CreateObservation(discovery);
            TaskbarPlacementResult placement = SafeRegionCalculator.Calculate(
                observation,
                TaskbarPlacementOptions.Default);
            var signature = TaskbarLiveScanSignature.Create(
                discovery,
                placement);
            VerifiedLiveLayout? layout = null;
            VerifiedFloatingContext? floatingContext = null;
            if (discovery.IsComplete &&
                discovery.Snapshot is TaskbarSnapshot snapshot)
            {
                if (snapshot.Monitor.IsPrimary &&
                    snapshot.Monitor.Bounds.IsValid &&
                    snapshot.Monitor.WorkArea.IsValid)
                {
                    floatingContext = new(
                        snapshot.Monitor.Bounds,
                        snapshot.Monitor.WorkArea,
                        snapshot.Dpi,
                        FloatingBounds: null);
                }

                if (observation is TaskbarLayoutObservation verifiedObservation &&
                    placement.Decision == PlacementDecision.Place &&
                    placement.Bounds is PixelRect bounds &&
                    placement.Mode is TaskbarStripMode mode)
                {
                    layout = new(
                        new TaskbarHostIdentity(
                            snapshot.TaskbarHandle,
                            snapshot.ExplorerProcessId,
                            snapshot.Dpi,
                            snapshot.Bounds),
                        bounds,
                        mode,
                        verifiedObservation,
                        continuityAnchor ??
                            TaskbarContinuityAnchor.FromCompleteDiscovery(discovery));
                }
            }

            return new(discovery, placement, layout, floatingContext, signature);
        }

        void RetainFreshFloatingContext(
            LiveLayoutScan scan,
            bool invalidatedDuringScan)
        {
            if (!invalidatedDuringScan &&
                scan.FloatingContext is VerifiedFloatingContext fresh)
            {
                retainedFloatingContext = fresh;
            }
        }

        bool ConsumeAutomationInvalidation()
        {
            if (!automationSignal.TryConsume(
                    ref observedAutomationGeneration,
                    out TaskbarAutomationInvalidation acceptedInvalidation))
            {
                return false;
            }

            VerifiedLiveLayout? current = currentNativeLayout;
            bool canAttemptContinuityPreflight =
                state == TaskbarPresentationState.NativeVisible &&
                current is not null &&
                nativeHost.IsCreated &&
                TaskbarNativeContinuityInvalidationPolicy.IsEligibleEvent(
                    acceptedInvalidation);
            Win32TaskbarContinuityProbe? nativePreflight = null;
            bool nativePreflightVerified =
                canAttemptContinuityPreflight &&
                TryVerifyVisibleNativePreflight(current!, out nativePreflight);
            bool canProbeContinuityWhileVisible =
                nativePreflightVerified &&
                TaskbarNativeContinuityInvalidationPolicy.CanProbeWhileVisible(
                    acceptedInvalidation,
                    nativePreflight!.Route,
                    lastContinuityRoute);
            if (canProbeContinuityWhileVisible)
            {
                nativeContinuityProbeRequested = true;
                invalidationGeneration = unchecked(invalidationGeneration + 1);
                automationInvalidation = acceptedInvalidation;
                return true;
            }

            if (state == TaskbarPresentationState.NativeVisible)
            {
                string admissionReason = acceptedInvalidation.RequiresHideFirst
                    ? "BatchRequiresHideFirst"
                    : !TaskbarNativeContinuityInvalidationPolicy.IsEligibleEvent(
                        acceptedInvalidation)
                        ? "EventNotEligible"
                        : !nativePreflightVerified
                            ? "NativePreflightRejected"
                            : "RouteNotEligible";
                Console.Error.WriteLine(
                    $"continuity-admission=rejected; " +
                    $"preflight-route={nativePreflight?.Route ?? TaskbarContinuityRoute.None}; " +
                    $"reason={admissionReason}");
            }

            if (nativeHost.IsCreated)
            {
                nativeHost.Hide();
            }

            automationLayoutInvalidated = true;
            invalidationGeneration = unchecked(invalidationGeneration + 1);
            automationInvalidation = acceptedInvalidation;
            return true;
        }

        void PumpBothHosts()
        {
            if (nativeHost.IsCreated)
            {
                _ = nativeHost.PumpMessages();
            }

            if (floatingHost.IsCreated)
            {
                _ = floatingHost.PumpMessages();
            }
        }

        void ApplySampleInteraction(
            TaskbarPresentationSurface source,
            NativeHostInteraction interaction)
        {
            TaskbarPresentationSurface activeSurface = state switch
            {
                TaskbarPresentationState.NativeVisible => TaskbarPresentationSurface.Native,
                TaskbarPresentationState.FloatingFallback => TaskbarPresentationSurface.Floating,
                _ => TaskbarPresentationSurface.None,
            };
            if (source != activeSurface)
            {
                return;
            }

            switch (interaction.Kind)
            {
                case NativeInteractionKind.DragStarted:
                case NativeInteractionKind.DragMoved:
                case NativeInteractionKind.DragCompleted:
                    if (SliderGeometry.TryCreate(
                            activeSurfaceBounds.Width,
                            activeSurfaceBounds.Height,
                            out SliderLayout layout))
                    {
                        sampleVolume = SliderGeometry.FractionFromPointerX(
                            layout,
                            interaction.X);
                    }

                    break;
                case NativeInteractionKind.Wheel:
                    sampleVolume = Math.Clamp(
                        sampleVolume + (Math.Sign(interaction.WheelDelta) * 0.02),
                        0,
                        1);
                    break;
                case NativeInteractionKind.CaptureLost:
                    break;
                default:
                    throw new InvalidOperationException("Unknown native host interaction.");
            }

            nativeHost.VolumeFraction = sampleVolume;
            floatingHost.VolumeFraction = sampleVolume;
            Console.Error.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"interaction={interaction.Kind}; sample-volume={sampleVolume:P0}"));
        }

        void DestroyNativeHost()
        {
            automationSignal.SetIgnoredWindowHandle(nint.Zero);
            if (nativeHost.IsCreated)
            {
                nativeHost.Hide();
                nativeHost.Destroy();
                Console.Error.WriteLine("native-host=destroyed");
            }

            currentNativeLayout = null;
            lastContinuityRoute = TaskbarContinuityRoute.None;
            if (state == TaskbarPresentationState.NativeVisible)
            {
                activeSurfaceBounds = default;
            }
        }

        void DestroyFloatingHost()
        {
            if (floatingHost.IsCreated)
            {
                floatingHost.Hide();
                floatingHost.Destroy();
                Console.Error.WriteLine("floating-host=destroyed");
            }

            currentFloatingContext = null;
            if (state == TaskbarPresentationState.FloatingFallback)
            {
                activeSurfaceBounds = default;
            }
        }

        void StopWatcher()
        {
            TaskbarAutomationWatcher? watcher = automationWatcher;
            automationWatcher = null;
            if (watcher is not null)
            {
                try
                {
                    watcher.Dispose();
                }
                catch (Exception exception) when (IsRecoverableWatcherFailure(exception))
                {
                    watcherDisabledForSession = true;
                    Console.Error.WriteLine(
                        $"native-watcher=stop-failed; kind={exception.GetType().Name}; " +
                        "session-disabled=True");
                }
            }

            observedAutomationGeneration = automationSignal.Generation;
        }

        void CleanupAllHosts()
        {
            try
            {
                DestroyNativeHost();
            }
            finally
            {
                try
                {
                    DestroyFloatingHost();
                }
                finally
                {
                    StopWatcher();
                }
            }
        }

        void ResetInvalidationFlags()
        {
            nativeLayoutInvalidated = false;
            nativeAttachmentUnavailable = false;
            automationLayoutInvalidated = false;
            floatingLayoutInvalidated = false;
            nativeContinuityProbeRequested = false;
            nativeInvalidationReason = null;
            automationInvalidation = null;
        }

        void WriteInvalidationSummary()
        {
            if (nativeAttachmentUnavailable)
            {
                Console.Error.WriteLine("layout-invalidated=NativeAttachmentUnavailable");
                return;
            }

            if (nativeInvalidationReason is NativeLayoutInvalidationReason reason)
            {
                Console.Error.WriteLine($"layout-invalidated={reason}");
                return;
            }

            TaskbarAutomationInvalidation classification =
                automationInvalidation ?? default;
            Console.Error.WriteLine(
                $"layout-invalidated=UiAutomationChanged; " +
                $"kind={classification.Kind}; source={classification.SourceClass}; " +
                $"batch-hide-first={classification.RequiresHideFirst}");
        }

        void SetState(
            TaskbarPresentationState nextState,
            PresentationTransitionReason reason)
        {
            if (state == nextState)
            {
                return;
            }

            Console.Error.WriteLine(
                $"presentation-transition={state}->{nextState}; reason={reason}");
            state = nextState;
        }

        TimeSpan Elapsed() => Stopwatch.GetElapsedTime(sessionStartTimestamp);

        bool NativePipelineAvailable() =>
            nativeCreationGuard.NativeAvailable && !watcherDisabledForSession;
    }

    private static bool RequiresFreshMonitorGeometry(
        NativeLayoutInvalidationReason reason) =>
        reason is
            NativeLayoutInvalidationReason.SettingsChanged or
            NativeLayoutInvalidationReason.DisplayChanged or
            NativeLayoutInvalidationReason.DpiChanged or
            NativeLayoutInvalidationReason.DpiChangedBeforeParent or
            NativeLayoutInvalidationReason.DpiChangedAfterParent;

    private static bool IsRecoverableNativeCreationFailure(Exception exception) =>
        exception is InvalidOperationException or Win32Exception;

    private static bool IsRecoverableWatcherFailure(Exception exception) =>
        exception is
            InvalidOperationException or
            TimeoutException or
            COMException or
            ElementNotAvailableException or
            ArgumentException or
            UnauthorizedAccessException or
            NotSupportedException or
            AggregateException;

    private static bool IsRecoverableFloatingFailure(Exception exception) =>
        exception is InvalidOperationException or Win32Exception;

    private static void WriteCreationSummary(NativeHostCreationSnapshot creation)
    {
        Console.Error.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"host-attached style={creation.StyleMode}; " +
                $"dpi-before={creation.BeforeParentingDpi.Process}/{creation.BeforeParentingDpi.WindowDpi}; " +
                $"dpi-after={creation.AfterParentingDpi.Process}/{creation.AfterParentingDpi.WindowDpi}; " +
                $"pmv2-before={creation.BeforeParentingDpi.ThreadIsPerMonitorV2}/" +
                $"{creation.BeforeParentingDpi.WindowIsPerMonitorV2}; " +
                $"pmv2-after={creation.AfterParentingDpi.ThreadIsPerMonitorV2}/" +
                $"{creation.AfterParentingDpi.WindowIsPerMonitorV2}"));
    }

    private static long AddDuration(long timestamp, TimeSpan duration)
    {
        double ticks = duration.TotalSeconds * Stopwatch.Frequency;
        return checked(timestamp + (long)Math.Ceiling(ticks));
    }

    private static TimeSpan RemainingUntil(long deadlineTimestamp)
    {
        long now = Stopwatch.GetTimestamp();
        return now >= deadlineTimestamp
            ? TimeSpan.Zero
            : Stopwatch.GetElapsedTime(now, deadlineTimestamp);
    }

    private sealed record VerifiedLiveLayout(
        TaskbarHostIdentity Identity,
        PixelRect Bounds,
        TaskbarStripMode Mode,
        TaskbarLayoutObservation Observation,
        TaskbarContinuityAnchor ContinuityAnchor)
    {
        internal NativePromotionCandidate ToPromotionCandidate() =>
            new(Identity, Bounds, Mode);
    }

    private sealed record VerifiedFloatingContext(
        PixelRect MonitorBounds,
        PixelRect WorkArea,
        uint Dpi,
        PixelRect? FloatingBounds);

    private sealed record LiveLayoutScan(
        TaskbarDiscoveryResult Discovery,
        TaskbarPlacementResult Placement,
        VerifiedLiveLayout? Layout,
        VerifiedFloatingContext? FloatingContext,
        TaskbarLiveScanSignature Signature);

    private sealed record NativeContinuityLiveScan(
        TaskbarContinuityDiscoveryResult Result,
        LiveLayoutScan? Scan);

    private enum PresentationTransitionReason
    {
        InitialFallback,
        InitialNative,
        NativeInvalidated,
        NativeAttachmentUnavailable,
        WatchdogUnsafe,
        ScanInvalidated,
        VerifiedNoFit,
        TransientUnknown,
        FloatingInvalidated,
        NativePromoted,
        NativeCreationFailed,
    }

    private sealed class HostDurationElapsedException : Exception
    {
    }
}
