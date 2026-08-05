using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Presentation;

internal enum TaskbarPresentationState
{
    Starting,
    NativeVisible,
    NativeRecoveryHidden,
    FloatingFallback,
    HiddenFallback,
}

internal enum TaskbarPresentationSurface
{
    None,
    Native,
    Floating,
}

internal readonly record struct TaskbarPresentationInput(
    TaskbarPresentationState CurrentState,
    PlacementDecision PlacementDecision,
    bool InvalidatedDuringScan,
    bool NativeAvailable,
    bool FloatingAvailable,
    bool NativePromotionReady,
    TimeSpan RecoveryElapsed);

internal readonly record struct TaskbarPresentationTransition(
    TaskbarPresentationState NextState,
    TaskbarPresentationSurface DesiredSurface,
    bool ProcessShouldContinue);

/// <summary>
/// Pure presentation policy. A native placement failure changes the visible
/// surface, never the process lifetime. Fatal process errors remain outside
/// this policy at the native resource boundary.
/// </summary>
internal static class TaskbarPresentationPolicy
{
    internal static readonly TimeSpan MaximumNativeRecoveryDuration =
        TimeSpan.FromSeconds(10);

    internal static TaskbarPresentationTransition Decide(TaskbarPresentationInput input)
    {
        Validate(input);
        if (!input.NativeAvailable)
        {
            return Fallback(input.FloatingAvailable);
        }

        PlacementDecision effectiveDecision = input.InvalidatedDuringScan
            ? PlacementDecision.TransientUnknown
            : input.PlacementDecision;
        return input.CurrentState switch
        {
            TaskbarPresentationState.Starting => DecideStarting(input, effectiveDecision),
            TaskbarPresentationState.NativeVisible => DecideNativeVisible(input, effectiveDecision),
            TaskbarPresentationState.NativeRecoveryHidden =>
                DecideNativeRecovery(input, effectiveDecision),
            TaskbarPresentationState.FloatingFallback or
            TaskbarPresentationState.HiddenFallback => DecideFallback(input, effectiveDecision),
            _ => throw new InvalidOperationException("Unknown presentation state."),
        };
    }

    private static TaskbarPresentationTransition DecideStarting(
        TaskbarPresentationInput input,
        PlacementDecision decision)
    {
        if (decision != PlacementDecision.Place)
        {
            return Fallback(input.FloatingAvailable);
        }

        return input.NativePromotionReady
            ? NativeVisible()
            : Continue(TaskbarPresentationState.Starting, TaskbarPresentationSurface.None);
    }

    private static TaskbarPresentationTransition DecideNativeVisible(
        TaskbarPresentationInput input,
        PlacementDecision decision)
    {
        return decision switch
        {
            PlacementDecision.Place => NativeVisible(),
            PlacementDecision.VerifiedNoFit => Fallback(input.FloatingAvailable),
            PlacementDecision.TransientUnknown when input.FloatingAvailable => Fallback(
                floatingAvailable: true),
            PlacementDecision.TransientUnknown => Continue(
                TaskbarPresentationState.NativeRecoveryHidden,
                TaskbarPresentationSurface.None),
            _ => throw new InvalidOperationException("Unknown placement decision."),
        };
    }

    private static TaskbarPresentationTransition DecideNativeRecovery(
        TaskbarPresentationInput input,
        PlacementDecision decision)
    {
        if (input.RecoveryElapsed >= MaximumNativeRecoveryDuration ||
            decision == PlacementDecision.VerifiedNoFit)
        {
            return Fallback(input.FloatingAvailable);
        }

        if (decision == PlacementDecision.Place && input.NativePromotionReady)
        {
            return NativeVisible();
        }

        return Continue(
            TaskbarPresentationState.NativeRecoveryHidden,
            TaskbarPresentationSurface.None);
    }

    private static TaskbarPresentationTransition DecideFallback(
        TaskbarPresentationInput input,
        PlacementDecision decision)
    {
        if (decision == PlacementDecision.Place && input.NativePromotionReady)
        {
            return NativeVisible();
        }

        return Fallback(input.FloatingAvailable);
    }

    private static TaskbarPresentationTransition NativeVisible() =>
        Continue(TaskbarPresentationState.NativeVisible, TaskbarPresentationSurface.Native);

    private static TaskbarPresentationTransition Fallback(bool floatingAvailable) =>
        floatingAvailable
            ? Continue(
                TaskbarPresentationState.FloatingFallback,
                TaskbarPresentationSurface.Floating)
            : Continue(
                TaskbarPresentationState.HiddenFallback,
                TaskbarPresentationSurface.None);

    private static TaskbarPresentationTransition Continue(
        TaskbarPresentationState state,
        TaskbarPresentationSurface surface) =>
        new(state, surface, ProcessShouldContinue: true);

    private static void Validate(TaskbarPresentationInput input)
    {
        if (!Enum.IsDefined(input.CurrentState))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "The presentation state is invalid.");
        }

        if (!Enum.IsDefined(input.PlacementDecision))
        {
            throw new ArgumentOutOfRangeException(nameof(input), "The placement decision is invalid.");
        }

        if (input.RecoveryElapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Recovery elapsed time cannot be negative.");
        }
    }
}
