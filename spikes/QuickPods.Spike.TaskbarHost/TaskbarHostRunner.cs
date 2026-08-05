using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Versioning;
using QuickPods.Spike.TaskbarHost.Diagnostics;
using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;
using QuickPods.Spike.TaskbarHost.Placement;
using QuickPods.Spike.TaskbarHost.Runtime;

namespace QuickPods.Spike.TaskbarHost;

internal sealed class TaskbarHostRunner
{
    private static readonly TimeSpan LayoutRescanInterval = TimeSpan.FromSeconds(5);
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
        using var host = new NativeTaskbarHost();
        bool layoutInvalidated = false;
        long invalidationGeneration = 0;
        bool forceRecreate = false;
        NativeLayoutInvalidationReason? invalidationReason = null;
        TaskbarAutomationInvalidation? automationInvalidation = null;
        TaskbarAutomationInvalidation? recoveryAutomationInvalidation = null;
        PixelRect currentBounds = default;
        TaskbarHostIdentity currentIdentity = default;
        var automationSignal = new TaskbarAutomationInvalidationSignal();
        var automationChurnGuard = new TaskbarAutomationChurnGuard();
        long automationChurnEpoch = Stopwatch.GetTimestamp();
        long observedAutomationGeneration = automationSignal.Generation;
        bool automationContinuousChurnFailedClosed = false;
        using var scanDiagnostics = new TaskbarLiveScanDiagnosticCoalescer(
            Console.Error.WriteLine);

        host.LayoutInvalidated += reason =>
        {
            layoutInvalidated = true;
            invalidationGeneration = unchecked(invalidationGeneration + 1);
            forceRecreate |= TaskbarRecoveryPolicy.RequiresForcedRecreation(reason);
            invalidationReason = reason;
            automationInvalidation = null;
            recoveryAutomationInvalidation = null;
        };
        host.Interaction += interaction => ApplySampleInteraction(host, currentBounds, interaction);

        LiveLayoutScan initialScan = DiscoverVerifiedLayout(host, cancellationToken);
        scanDiagnostics.Record(TaskbarLiveScanStage.Initial, initialScan.Signature);
        VerifiedLiveLayout? initialLayout = initialScan.Layout;
        if (initialLayout is null)
        {
            Console.Error.WriteLine("The live host was not created because the second safety scan was not Place.");
            return false;
        }

        var automationWatcher = TaskbarAutomationWatcher.Start(
            initialLayout.Identity.TaskbarHandle,
            automationSignal);
        try
        {
            long armingStartTimestamp = Stopwatch.GetTimestamp();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(armingStartTimestamp) >=
                    TaskbarRecoveryPolicy.MaximumRecoveryDuration)
                {
                    Console.Error.WriteLine(
                        "The live host was not created because the watched safety scan did not stabilize.");
                    return false;
                }

                long generationBeforeScan = automationSignal.Generation;
                LiveLayoutScan armedScan = DiscoverVerifiedLayout(host, cancellationToken);
                long generationAfterScan = automationSignal.Generation;
                bool armedInvalidatedDuringScan = generationBeforeScan != generationAfterScan;
                scanDiagnostics.Record(
                    TaskbarLiveScanStage.Armed,
                    armedScan.Signature.WithInvalidatedDuringScan(armedInvalidatedDuringScan));
                VerifiedLiveLayout? armedLayout = armedScan.Layout;
                if (armedLayout is null)
                {
                    Thread.Sleep(TaskbarRecoveryPolicy.RetryInterval);
                    continue;
                }

                if (armedLayout.Identity.TaskbarHandle != automationWatcher.TaskbarHandle)
                {
                    _ = automationWatcher.ReplaceTaskbar(armedLayout.Identity.TaskbarHandle);
                    continue;
                }

                if (!TaskbarAutomationArmingFence.IsStable(
                        automationWatcher.TaskbarHandle,
                        armedLayout.Identity.TaskbarHandle,
                        generationBeforeScan,
                        generationAfterScan))
                {
                    Thread.Sleep(TaskbarRecoveryPolicy.RetryInterval);
                    continue;
                }

                observedAutomationGeneration = generationAfterScan;
                initialLayout = armedLayout;
                break;
            }

            NativeParentStyleMode styleMode = options.Style switch
            {
                RequestedHostStyle.Child => NativeParentStyleMode.Child,
                RequestedHostStyle.Popup => NativeParentStyleMode.PopupPreserved,
                _ => throw new InvalidOperationException("Unknown native host style."),
            };

            NativeHostCreationSnapshot creation = host.Create(
                initialLayout.Identity.TaskbarHandle,
                initialLayout.Bounds,
                styleMode);
            currentIdentity = initialLayout.Identity;
            currentBounds = initialLayout.Bounds;
            automationSignal.SetIgnoredWindowHandle(host.WindowHandle);
            _ = ConsumeAutomationInvalidation();
            WriteCreationSummary(creation);

            long startTimestamp = Stopwatch.GetTimestamp();
            long nextRescanTimestamp = AddDuration(startTimestamp, LayoutRescanInterval);
            long recoveryStartTimestamp = 0;
            long nextRecoveryAttemptTimestamp = 0;
            int recoveryAttemptCount = 0;
            bool recoveryInProgress = false;
            bool failedClosed = false;
            while (Stopwatch.GetElapsedTime(startTimestamp) < options.Duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = ConsumeAutomationInvalidation();
                if (automationContinuousChurnFailedClosed)
                {
                    failedClosed = true;
                    break;
                }

                _ = host.PumpMessages();
                _ = ConsumeAutomationInvalidation();
                if (automationContinuousChurnFailedClosed)
                {
                    failedClosed = true;
                    break;
                }

                long now = Stopwatch.GetTimestamp();
                if (layoutInvalidated)
                {
                    host.Hide();
                    WriteInvalidationSummary();
                    if (!recoveryInProgress)
                    {
                        recoveryInProgress = true;
                        recoveryStartTimestamp = now;
                        nextRecoveryAttemptTimestamp = now;
                        recoveryAttemptCount = 0;
                    }

                    layoutInvalidated = false;
                    invalidationReason = null;
                    automationInvalidation = null;
                }
                else if (!recoveryInProgress && now >= nextRescanTimestamp)
                {
                    long watchdogScanStartTimestamp = Stopwatch.GetTimestamp();
                    long watchdogInvalidationGeneration = invalidationGeneration;
                    LiveLayoutScan watchdogScan = DiscoverVerifiedLayout(
                        host,
                        cancellationToken,
                        () => _ = ConsumeAutomationInvalidation());
                    _ = host.PumpMessages();
                    _ = ConsumeAutomationInvalidation();
                    if (automationContinuousChurnFailedClosed)
                    {
                        bool invalidatedDuringFailedWatchdogScan =
                            layoutInvalidated ||
                            invalidationGeneration != watchdogInvalidationGeneration;
                        scanDiagnostics.Record(
                            TaskbarLiveScanStage.Watchdog,
                            watchdogScan.Signature.WithInvalidatedDuringScan(
                                invalidatedDuringFailedWatchdogScan));
                        failedClosed = true;
                        break;
                    }

                    bool watchdogInvalidatedDuringScan =
                        layoutInvalidated ||
                        invalidationGeneration != watchdogInvalidationGeneration;
                    scanDiagnostics.Record(
                        TaskbarLiveScanStage.Watchdog,
                        watchdogScan.Signature.WithInvalidatedDuringScan(
                            watchdogInvalidatedDuringScan));
                    VerifiedLiveLayout? watchdogLayout = watchdogScan.Layout;
                    bool watchdogCurrentBoundsSafe =
                        watchdogLayout is not null &&
                        SafeRegionCalculator.IsExistingPlacementSafe(
                            watchdogLayout.Observation,
                            TaskbarPlacementOptions.Default,
                            currentBounds);
                    bool watchdogNativeAttachmentValid = host.IsCurrentAttachmentValid();
                    TaskbarWatchdogDecision watchdogDecision = TaskbarWatchdogPolicy.Decide(new(
                        currentIdentity,
                        currentBounds,
                        watchdogLayout?.Identity,
                        watchdogLayout?.Bounds,
                        watchdogInvalidatedDuringScan,
                        watchdogCurrentBoundsSafe,
                        watchdogNativeAttachmentValid));

                    if (watchdogDecision == TaskbarWatchdogDecision.KeepVisible)
                    {
                        Console.Error.WriteLine(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"layout-watchdog=verified-visible; " +
                                     $"elapsed-ms={Stopwatch.GetElapsedTime(watchdogScanStartTimestamp).TotalMilliseconds:F3}; " +
                                     $"current-bounds-safe=True"));
                        nextRescanTimestamp = AddDuration(
                            Stopwatch.GetTimestamp(),
                            LayoutRescanInterval);
                    }
                    else
                    {
                        host.Hide();
                        forceRecreate |= !watchdogNativeAttachmentValid;
                        if (!watchdogInvalidatedDuringScan)
                        {
                            recoveryInProgress = true;
                            recoveryStartTimestamp = Stopwatch.GetTimestamp();
                            nextRecoveryAttemptTimestamp = recoveryStartTimestamp;
                            recoveryAttemptCount = 0;
                            recoveryAutomationInvalidation = null;
                        }
                    }
                }

                if (recoveryInProgress && now >= nextRecoveryAttemptTimestamp)
                {
                    recoveryAttemptCount = checked(recoveryAttemptCount + 1);
                    long scanInvalidationGeneration = invalidationGeneration;
                    LiveLayoutScan recoveryScan = DiscoverVerifiedLayout(host, cancellationToken);
                    VerifiedLiveLayout? verified = recoveryScan.Layout;
                    _ = host.PumpMessages();
                    _ = ConsumeAutomationInvalidation();
                    if (automationContinuousChurnFailedClosed)
                    {
                        RecordRecoveryScanDiagnostic();
                        failedClosed = true;
                        break;
                    }

                    if (verified is not null &&
                        verified.Identity.TaskbarHandle != automationWatcher.TaskbarHandle)
                    {
                        host.Hide();
                        _ = automationWatcher.ReplaceTaskbar(verified.Identity.TaskbarHandle);
                        forceRecreate = true;
                        layoutInvalidated = true;
                        invalidationGeneration = unchecked(invalidationGeneration + 1);
                        invalidationReason = null;
                        automationInvalidation = null;
                        recoveryAutomationInvalidation = null;
                        _ = ConsumeAutomationInvalidation();
                        if (automationContinuousChurnFailedClosed)
                        {
                            RecordRecoveryScanDiagnostic();
                            failedClosed = true;
                            break;
                        }
                    }

                    if (Stopwatch.GetElapsedTime(startTimestamp) >= options.Duration)
                    {
                        RecordRecoveryScanDiagnostic();
                        break;
                    }

                    bool invalidatedDuringScan =
                        layoutInvalidated || invalidationGeneration != scanInvalidationGeneration;
                    scanDiagnostics.Record(
                        TaskbarLiveScanStage.Recovery,
                        recoveryScan.Signature.WithInvalidatedDuringScan(invalidatedDuringScan));
                    if (invalidatedDuringScan)
                    {
                        host.Hide();
                        layoutInvalidated = false;
                        invalidationReason = null;
                        automationInvalidation = null;
                    }

                    TimeSpan recoveryElapsed = Stopwatch.GetElapsedTime(recoveryStartTimestamp);
                    bool identityChangedForDecision =
                        verified is not null && currentIdentity != verified.Identity;
                    PixelRect previousBoundsForDecision = currentBounds;
                    bool boundsChangedForDecision =
                        verified is not null && currentBounds != verified.Bounds;
                    bool currentBoundsSafeForDecision =
                        verified is not null && SafeRegionCalculator.IsExistingPlacementSafe(
                            verified.Observation,
                            TaskbarPlacementOptions.Default,
                            currentBounds);
                    bool forcedForDecision = forceRecreate;
                    PixelRect? discoveredBoundsForDecision = verified?.Bounds;
                    TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(new(
                        currentIdentity,
                        currentBounds,
                        verified?.Identity,
                        verified?.Bounds,
                        forceRecreate,
                        invalidatedDuringScan,
                        recoveryElapsed,
                        currentBoundsSafeForDecision));

                    TaskbarAutomationSparseChurnDecision sparseChurnDecision =
                        TaskbarAutomationSparseChurnPolicy.Decide(new(
                            automationChurnGuard.SparseLimitReached,
                            recoveryAutomationInvalidation,
                            HasFreshPlaceObservation: verified is not null,
                            SameIdentity: verified is not null && currentIdentity == verified.Identity,
                            ForceRecreate: forcedForDecision,
                            InvalidatedDuringScan: invalidatedDuringScan,
                            CurrentBoundsRemainSafe: currentBoundsSafeForDecision,
                            RecoveryDecision: decision));
                    if (sparseChurnDecision == TaskbarAutomationSparseChurnDecision.Acknowledge)
                    {
                        automationChurnGuard.AcknowledgeSparseLimit();
                    }
                    else if (sparseChurnDecision == TaskbarAutomationSparseChurnDecision.FailClosed)
                    {
                        scanDiagnostics.Flush();
                        Console.Error.WriteLine(
                            "layout-recovery=ui-automation-churn; host remains hidden");
                        failedClosed = true;
                        break;
                    }

                    switch (decision)
                    {
                        case TaskbarRecoveryDecision.RetryHidden:
                            nextRecoveryAttemptTimestamp = AddDuration(
                                Stopwatch.GetTimestamp(),
                                TaskbarRecoveryPolicy.RetryInterval);
                            break;
                        case TaskbarRecoveryDecision.ShowVerifiedExisting:
                            try
                            {
                                host.Show();
                                _ = ConsumeAutomationInvalidation();
                                CompleteRecovery(recreated: false);
                            }
                            catch (NativeLayoutInvalidatedException)
                            {
                                host.Hide();
                                forceRecreate = true;
                                ScheduleRetryAfterNativeRace();
                            }

                            break;
                        case TaskbarRecoveryDecision.RecreateVerified:
                            host.Destroy();
                            try
                            {
                                creation = host.Create(
                                    verified!.Identity.TaskbarHandle,
                                    verified.Bounds,
                                    styleMode);
                                currentIdentity = verified.Identity;
                                currentBounds = verified.Bounds;
                                automationSignal.SetIgnoredWindowHandle(host.WindowHandle);
                                _ = ConsumeAutomationInvalidation();
                                WriteCreationSummary(creation);
                                CompleteRecovery(recreated: true);
                            }
                            catch (NativeLayoutInvalidatedException)
                            {
                                host.Destroy();
                                forceRecreate = true;
                                ScheduleRetryAfterNativeRace();
                            }

                            break;
                        case TaskbarRecoveryDecision.FailClosed:
                            scanDiagnostics.Flush();
                            WriteRecoverySummary("timeout", recreated: false);
                            failedClosed = true;
                            break;
                        default:
                            throw new InvalidOperationException("Unknown taskbar recovery decision.");
                    }

                    if (failedClosed)
                    {
                        break;
                    }

                    void CompleteRecovery(bool recreated)
                    {
                        if (automationContinuousChurnFailedClosed)
                        {
                            host.Hide();
                            failedClosed = true;
                            return;
                        }

                        // No message can be dispatched between the post-scan pump and
                        // this action on the owning thread. If a synchronous native call
                        // delivered another invalidation, its WndProc already hid the
                        // view and this check keeps the recovery window active.
                        if (layoutInvalidated ||
                            invalidationGeneration != scanInvalidationGeneration)
                        {
                            host.Hide();
                            layoutInvalidated = false;
                            invalidationReason = null;
                            automationInvalidation = null;
                            nextRecoveryAttemptTimestamp = AddDuration(
                                Stopwatch.GetTimestamp(),
                                TaskbarRecoveryPolicy.RetryInterval);
                            return;
                        }

                        recoveryInProgress = false;
                        forceRecreate = false;
                        recoveryAutomationInvalidation = null;
                        scanDiagnostics.Flush();
                        WriteRecoverySummary("verified", recreated);
                        nextRescanTimestamp = AddDuration(
                            Stopwatch.GetTimestamp(),
                            LayoutRescanInterval);
                    }

                    void RecordRecoveryScanDiagnostic()
                    {
                        bool scanWasInvalidated =
                            layoutInvalidated ||
                            invalidationGeneration != scanInvalidationGeneration;
                        scanDiagnostics.Record(
                            TaskbarLiveScanStage.Recovery,
                            recoveryScan.Signature.WithInvalidatedDuringScan(
                                scanWasInvalidated));
                    }

                    void ScheduleRetryAfterNativeRace()
                    {
                        Console.Error.WriteLine(
                            "layout-revalidation=native-invalidation-race; host remains hidden");
                        nextRecoveryAttemptTimestamp = AddDuration(
                            Stopwatch.GetTimestamp(),
                            TaskbarRecoveryPolicy.RetryInterval);
                    }

                    void WriteRecoverySummary(string outcome, bool recreated)
                    {
                        Console.Error.WriteLine(
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"layout-recovery={outcome}; " +
                                     $"elapsed-ms={Stopwatch.GetElapsedTime(recoveryStartTimestamp).TotalMilliseconds:F3}; " +
                                     $"attempts={recoveryAttemptCount}; recreated={recreated}; " +
                                     $"identity-changed={identityChangedForDecision}; " +
                                     $"bounds-changed={boundsChangedForDecision}; " +
                                     $"current-bounds-safe={currentBoundsSafeForDecision}; " +
                                     $"forced={forcedForDecision}; " +
                                     $"previous-bounds={FormatBounds(previousBoundsForDecision)}; " +
                                     $"discovered-bounds={FormatBounds(discoveredBoundsForDecision)}"));
                    }
                }

                Thread.Sleep(MessagePumpInterval);
            }

            return TaskbarRecoveryPolicy.IsSuccessfulDurationCompletion(
                recoveryInProgress,
                failedClosed);

            bool ConsumeAutomationInvalidation()
            {
                if (!automationSignal.TryConsume(
                        ref observedAutomationGeneration,
                        out TaskbarAutomationInvalidation acceptedInvalidation))
                {
                    return false;
                }

                if (host.IsCreated)
                {
                    host.Hide();
                }

                layoutInvalidated = true;
                invalidationGeneration = unchecked(invalidationGeneration + 1);
                invalidationReason = null;
                automationInvalidation = acceptedInvalidation;
                recoveryAutomationInvalidation = acceptedInvalidation;
                TaskbarAutomationChurnLimit churnLimits = automationChurnGuard.RecordSignal(
                    Stopwatch.GetElapsedTime(automationChurnEpoch));
                bool continuousLimitReached =
                    (churnLimits & TaskbarAutomationChurnLimit.Continuous) != 0;
                if (continuousLimitReached && !automationContinuousChurnFailedClosed)
                {
                    Console.Error.WriteLine(
                        "layout-recovery=ui-automation-churn; host remains hidden");
                }

                automationContinuousChurnFailedClosed |= continuousLimitReached;
                return true;
            }

            void WriteInvalidationSummary()
            {
                if (invalidationReason is NativeLayoutInvalidationReason nativeReason)
                {
                    Console.Error.WriteLine($"layout-invalidated={nativeReason}");
                    return;
                }

                TaskbarAutomationInvalidation classification =
                    automationInvalidation ?? default;
                Console.Error.WriteLine(
                    $"layout-invalidated=UiAutomationChanged; " +
                    $"kind={classification.Kind}; source={classification.SourceClass}");
            }
        }
        finally
        {
            try
            {
                if (host.IsCreated)
                {
                    host.Hide();
                }
            }
            finally
            {
                try
                {
                    automationWatcher.Dispose();
                }
                finally
                {
                    if (host.IsCreated)
                    {
                        host.Destroy();
                    }
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static LiveLayoutScan DiscoverVerifiedLayout(
        NativeTaskbarHost host,
        CancellationToken cancellationToken,
        Action? consumeAutomationInvalidation = null)
    {
        var discoveryService = new TaskbarDiscoveryService(
            host.IsCreated ? host.WindowHandle : nint.Zero);
        Task<TaskbarDiscoveryResult> task = discoveryService.DiscoverAsync(cancellationToken);
        while (!task.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (host.IsCreated)
            {
                _ = host.PumpMessages();
                consumeAutomationInvalidation?.Invoke();
            }

            Thread.Sleep(MessagePumpInterval);
        }

        TaskbarDiscoveryResult discovery = task.GetAwaiter().GetResult();
        TaskbarLayoutObservation? observation = TaskbarLayoutAdapter.CreateObservation(discovery);
        TaskbarPlacementResult placement = SafeRegionCalculator.Calculate(
            observation,
            TaskbarPlacementOptions.Default);
        var signature = TaskbarLiveScanSignature.Create(
            discovery,
            placement);
        if (!discovery.IsComplete ||
            discovery.Snapshot is not TaskbarSnapshot snapshot ||
            observation is not TaskbarLayoutObservation verifiedObservation ||
            placement.Decision != PlacementDecision.Place ||
            placement.Bounds is not PixelRect bounds)
        {
            return new LiveLayoutScan(null, signature);
        }

        return new LiveLayoutScan(
            new VerifiedLiveLayout(
                new TaskbarHostIdentity(
                    snapshot.TaskbarHandle,
                    snapshot.ExplorerProcessId,
                    snapshot.Dpi,
                    snapshot.Bounds),
                bounds,
                verifiedObservation),
            signature);
    }

    private static void ApplySampleInteraction(
        NativeTaskbarHost host,
        PixelRect currentBounds,
        NativeHostInteraction interaction)
    {
        switch (interaction.Kind)
        {
            case NativeInteractionKind.DragStarted:
            case NativeInteractionKind.DragMoved:
            case NativeInteractionKind.DragCompleted:
                if (SliderGeometry.TryCreate(
                        currentBounds.Width,
                        currentBounds.Height,
                        out SliderLayout layout))
                {
                    host.VolumeFraction = SliderGeometry.FractionFromPointerX(
                        layout,
                        interaction.X);
                }

                break;
            case NativeInteractionKind.Wheel:
                host.VolumeFraction = Math.Clamp(
                    host.VolumeFraction + (Math.Sign(interaction.WheelDelta) * 0.02),
                    0,
                    1);
                break;
            case NativeInteractionKind.CaptureLost:
                break;
            default:
                throw new InvalidOperationException("Unknown native host interaction.");
        }

        Console.Error.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"interaction={interaction.Kind}; sample-volume={host.VolumeFraction:P0}"));
    }

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

    private static string FormatBounds(PixelRect? bounds)
    {
        return bounds is PixelRect value
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"({value.Left},{value.Top},{value.Width},{value.Height})")
            : "none";
    }

    private sealed record VerifiedLiveLayout(
        TaskbarHostIdentity Identity,
        PixelRect Bounds,
        TaskbarLayoutObservation Observation);

    private sealed record LiveLayoutScan(
        VerifiedLiveLayout? Layout,
        TaskbarLiveScanSignature Signature);
}
