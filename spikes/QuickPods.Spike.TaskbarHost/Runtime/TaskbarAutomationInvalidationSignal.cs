using QuickPods.Spike.TaskbarHost.Discovery;

namespace QuickPods.Spike.TaskbarHost.Runtime;

/// <summary>
/// Coalesces UI Automation callbacks into an atomic generation counter. Raw
/// HWNDs are retained only in memory so events raised by the hosted QuickPods
/// view do not cause a hide/show feedback loop.
/// </summary>
internal sealed class TaskbarAutomationInvalidationSignal
{
    private AcceptedInvalidationState acceptedState = new(
        0,
        default,
        RequiresHideFirst: false);
    private long ignoredWindowHandle;

    internal long Generation => Volatile.Read(ref acceptedState).Generation;

    internal void SetIgnoredWindowHandle(nint windowHandle)
    {
        Interlocked.Exchange(ref ignoredWindowHandle, windowHandle.ToInt64());
    }

    internal void Signal(TaskbarAutomationEventSource source)
    {
        bool ownedSource =
            (source.ProcessId != 0 && source.ProcessId == Environment.ProcessId) ||
            (source.NativeWindowHandle != 0 &&
                source.NativeWindowHandle == unchecked((int)Interlocked.Read(ref ignoredWindowHandle)));
        TaskbarAutomationEventSourceClass sourceClass = ownedSource
            ? TaskbarAutomationEventSourceClass.Owned
            : source.ProcessId != 0 || source.NativeWindowHandle != 0
                ? TaskbarAutomationEventSourceClass.External
                : TaskbarAutomationEventSourceClass.Unknown;
        if (ownedSource && source.Kind != TaskbarAutomationEventKind.BoundingRectangleChanged)
        {
            return;
        }

        var accepted = new TaskbarAutomationInvalidation(source.Kind, sourceClass);
        bool requiresHideFirst =
            !TaskbarNativeContinuityInvalidationPolicy.IsEligibleEvent(accepted);
        AcceptedInvalidationState current;
        AcceptedInvalidationState replacement;
        do
        {
            current = Volatile.Read(ref acceptedState);
            replacement = new(
                unchecked(current.Generation + 1),
                accepted,
                current.RequiresHideFirst || requiresHideFirst);
        }
        while (!ReferenceEquals(
            Interlocked.CompareExchange(ref acceptedState, replacement, current),
            current));
    }

    internal bool TryConsume(ref long observedGeneration)
    {
        return TryConsume(ref observedGeneration, out _);
    }

    internal bool TryConsume(
        ref long observedGeneration,
        out TaskbarAutomationInvalidation invalidation)
    {
        AcceptedInvalidationState current = Volatile.Read(ref acceptedState);
        if (current.Generation == observedGeneration)
        {
            invalidation = default;
            return false;
        }

        observedGeneration = current.Generation;
        invalidation = current.Invalidation with
        {
            RequiresHideFirst = current.RequiresHideFirst,
        };
        AcceptedInvalidationState consumed = current with { RequiresHideFirst = false };
        _ = Interlocked.CompareExchange(ref acceptedState, consumed, current);
        return true;
    }

    private sealed record AcceptedInvalidationState(
        long Generation,
        TaskbarAutomationInvalidation Invalidation,
        bool RequiresHideFirst);
}

internal readonly record struct TaskbarAutomationInvalidation(
    TaskbarAutomationEventKind Kind,
    TaskbarAutomationEventSourceClass SourceClass,
    bool RequiresHideFirst = false);

internal readonly record struct TaskbarAutomationEventSource(
    int NativeWindowHandle,
    int ProcessId,
    TaskbarAutomationEventKind Kind = TaskbarAutomationEventKind.StructureChanged);

internal enum TaskbarAutomationEventKind
{
    StructureChanged,
    BoundingRectangleChanged,
    IsOffscreenChanged,
}

internal enum TaskbarAutomationEventSourceClass
{
    Unknown,
    Owned,
    External,
}

/// <summary>
/// Only a known external structure notification that coincides with the exact
/// retained taskbar disappearing from top-level enumeration may start a
/// bounded visible-continuity scan. A verified direct route may also transition
/// back to the enumerated route without a needless hide. All normal enumerated
/// structure changes, bounds, offscreen, unknown-source and owned-source
/// notifications retain the existing hide-first behavior.
/// </summary>
internal static class TaskbarNativeContinuityInvalidationPolicy
{
    internal static bool IsEligibleEvent(TaskbarAutomationInvalidation invalidation) =>
        !invalidation.RequiresHideFirst &&
        invalidation.Kind == TaskbarAutomationEventKind.StructureChanged &&
        invalidation.SourceClass == TaskbarAutomationEventSourceClass.External;

    internal static bool CanProbeWhileVisible(
        TaskbarAutomationInvalidation invalidation,
        TaskbarContinuityRoute preflightRoute,
        TaskbarContinuityRoute lastVerifiedRoute) =>
        IsEligibleEvent(invalidation) &&
        CanRetainVisibleRoute(preflightRoute, lastVerifiedRoute);

    internal static bool CanRetainVisibleRoute(
        TaskbarContinuityRoute preflightRoute,
        TaskbarContinuityRoute lastVerifiedRoute) =>
        preflightRoute == TaskbarContinuityRoute.DirectExpected ||
            (lastVerifiedRoute == TaskbarContinuityRoute.DirectExpected &&
                preflightRoute == TaskbarContinuityRoute.EnumeratedExpected);
}

internal static class TaskbarAutomationArmingFence
{
    internal static bool IsStable(
        nint watchedTaskbarHandle,
        nint discoveredTaskbarHandle,
        long generationBeforeScan,
        long generationAfterScan) =>
        watchedTaskbarHandle != nint.Zero &&
        watchedTaskbarHandle == discoveredTaskbarHandle &&
        generationBeforeScan == generationAfterScan;
}
