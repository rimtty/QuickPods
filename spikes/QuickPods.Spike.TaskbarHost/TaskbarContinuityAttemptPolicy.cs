using QuickPods.Spike.TaskbarHost.Diagnostics;
using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost;

internal readonly record struct TaskbarContinuityScanFence(
    long AutomationGeneration,
    long InvalidationGeneration)
{
    internal bool IsBroken(TaskbarContinuityScanCompletion completion) =>
        AutomationGeneration != completion.AutomationGeneration ||
        InvalidationGeneration != completion.InvalidationGeneration ||
        completion.NativeLayoutInvalidated ||
        completion.AutomationLayoutInvalidated ||
        completion.FloatingLayoutInvalidated ||
        completion.ContinuityProbeRequested;
}

internal readonly record struct TaskbarContinuityScanCompletion(
    long AutomationGeneration,
    long InvalidationGeneration,
    bool NativeLayoutInvalidated,
    bool AutomationLayoutInvalidated,
    bool FloatingLayoutInvalidated,
    bool ContinuityProbeRequested);

internal readonly record struct TaskbarContinuityInFlightInvalidation(
    NativeLayoutInvalidationReason? NativeReason,
    bool NativeLayoutInvalidated,
    bool AutomationLayoutInvalidated,
    bool FloatingLayoutInvalidated);

internal readonly record struct TaskbarContinuityAttemptInput(
    TaskbarContinuityScanFence Fence,
    TaskbarContinuityScanCompletion Completion,
    bool DiscoveryVerified,
    TaskbarContinuityEvidence Evidence,
    TaskbarLayoutObservation? PreviousObservation,
    TaskbarLayoutObservation? FreshObservation,
    TaskbarHostIdentity CurrentIdentity,
    PixelRect CurrentHostBounds,
    TaskbarHostIdentity? DiscoveredIdentity,
    PixelRect? DiscoveredHostBounds,
    bool NativeAttachmentValid);

internal sealed record TaskbarContinuityAttemptDecision(
    TaskbarWatchdogDecision WatchdogDecision,
    TaskbarLayoutObservation? SafetyObservation,
    bool InvalidatedDuringScan);

/// <summary>
/// Deterministic seam for the ordered Win32, UIA, and watcher outcome consumed
/// by the native-visible continuity runner. It keeps the race fence and the
/// conservative obstacle union in one fail-closed decision boundary.
/// </summary>
internal static class TaskbarContinuityAttemptPolicy
{
    internal static bool RequiresImmediateHide(
        TaskbarContinuityInFlightInvalidation invalidation) =>
        invalidation.NativeReason is not null ||
        invalidation.NativeLayoutInvalidated ||
        invalidation.AutomationLayoutInvalidated ||
        invalidation.FloatingLayoutInvalidated;

    internal static TaskbarContinuityAttemptDecision Decide(
        TaskbarContinuityAttemptInput input)
    {
        bool invalidatedDuringScan = input.Fence.IsBroken(input.Completion);
        TaskbarLayoutObservation? safetyObservation = input.DiscoveryVerified
            ? TaskbarLayoutAdapter.CreateContinuitySafetyObservation(
                input.Evidence,
                input.FreshObservation,
                input.PreviousObservation)
            : null;
        bool currentBoundsSafe =
            safetyObservation is not null &&
            SafeRegionCalculator.IsExistingPlacementSafe(
                safetyObservation,
                TaskbarPlacementOptions.Default,
                input.CurrentHostBounds);
        TaskbarWatchdogDecision watchdogDecision = TaskbarWatchdogPolicy.Decide(new(
            input.CurrentIdentity,
            input.CurrentHostBounds,
            input.DiscoveredIdentity,
            input.DiscoveredHostBounds,
            invalidatedDuringScan,
            currentBoundsSafe,
            input.NativeAttachmentValid));

        return new(
            watchdogDecision,
            safetyObservation,
            invalidatedDuringScan);
    }
}
