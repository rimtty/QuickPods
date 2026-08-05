using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;

namespace QuickPods.Spike.TaskbarHost;

internal readonly record struct TaskbarHostIdentity(
    nint TaskbarHandle,
    uint ExplorerProcessId,
    uint Dpi,
    PixelRect TaskbarBounds);

internal enum TaskbarRecoveryDecision
{
    RetryHidden,
    ShowVerifiedExisting,
    RecreateVerified,
    FailClosed,
}

internal readonly record struct TaskbarRecoveryInput(
    TaskbarHostIdentity CurrentIdentity,
    PixelRect CurrentHostBounds,
    TaskbarHostIdentity? DiscoveredIdentity,
    PixelRect? DiscoveredHostBounds,
    bool ForceRecreate,
    bool InvalidatedDuringScan,
    TimeSpan RecoveryElapsed,
    bool CurrentBoundsRemainSafe)
{
    /// <summary>
    /// Keeps equal verified preferred bounds source-compatible with callers that
    /// predate sticky placement. A differing rectangle must always supply an
    /// explicit fresh safety result.
    /// </summary>
    internal TaskbarRecoveryInput(
        TaskbarHostIdentity CurrentIdentity,
        PixelRect CurrentHostBounds,
        TaskbarHostIdentity? DiscoveredIdentity,
        PixelRect? DiscoveredHostBounds,
        bool ForceRecreate,
        bool InvalidatedDuringScan,
        TimeSpan RecoveryElapsed)
        : this(
            CurrentIdentity,
            CurrentHostBounds,
            DiscoveredIdentity,
            DiscoveredHostBounds,
            ForceRecreate,
            InvalidatedDuringScan,
            RecoveryElapsed,
            CurrentBoundsRemainSafe: DiscoveredHostBounds is PixelRect discoveredBounds &&
                CurrentHostBounds == discoveredBounds)
    {
    }
}

/// <summary>
/// Pure recovery decisions shared by the native runner and deterministic tests.
/// The policy never permits stale or partially discovered geometry to be shown.
/// </summary>
internal static class TaskbarRecoveryPolicy
{
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan MaximumRecoveryDuration = TimeSpan.FromSeconds(10);

    internal static TaskbarRecoveryDecision Decide(TaskbarRecoveryInput input)
    {
        if (input.RecoveryElapsed >= MaximumRecoveryDuration)
        {
            return TaskbarRecoveryDecision.FailClosed;
        }

        if (input.InvalidatedDuringScan ||
            input.DiscoveredIdentity is not TaskbarHostIdentity discoveredIdentity ||
            input.DiscoveredHostBounds is not PixelRect discoveredHostBounds ||
            !discoveredHostBounds.IsValid)
        {
            return TaskbarRecoveryDecision.RetryHidden;
        }

        if (input.ForceRecreate ||
            input.CurrentIdentity != discoveredIdentity ||
            !input.CurrentHostBounds.IsValid ||
            !input.CurrentBoundsRemainSafe)
        {
            return TaskbarRecoveryDecision.RecreateVerified;
        }

        return TaskbarRecoveryDecision.ShowVerifiedExisting;
    }

    internal static bool IsSuccessfulDurationCompletion(
        bool recoveryInProgress,
        bool failedClosed) =>
        !recoveryInProgress && !failedClosed;

    internal static bool RequiresForcedRecreation(NativeLayoutInvalidationReason reason) =>
        reason is NativeLayoutInvalidationReason.TaskbarCreated or
            NativeLayoutInvalidationReason.DisplayChanged or
            NativeLayoutInvalidationReason.DpiChanged or
            NativeLayoutInvalidationReason.DpiChangedBeforeParent or
            NativeLayoutInvalidationReason.DpiChangedAfterParent;
}
