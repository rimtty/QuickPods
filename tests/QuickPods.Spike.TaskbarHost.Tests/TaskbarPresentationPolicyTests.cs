using QuickPods.Spike.TaskbarHost.Placement;
using QuickPods.Spike.TaskbarHost.Presentation;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class TaskbarPresentationPolicyTests
{
    [Fact]
    public void Starting_PlaceNeedsPromotionBeforeNativeBecomesVisible()
    {
        TaskbarPresentationTransition waiting = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.Starting,
            PlacementDecision.Place,
            nativePromotionReady: false));
        TaskbarPresentationTransition promoted = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.Starting,
            PlacementDecision.Place,
            nativePromotionReady: true));

        AssertTransition(
            waiting,
            TaskbarPresentationState.Starting,
            TaskbarPresentationSurface.None);
        AssertTransition(
            promoted,
            TaskbarPresentationState.NativeVisible,
            TaskbarPresentationSurface.Native);
    }

    [Theory]
    [InlineData((int)PlacementDecision.VerifiedNoFit, true, (int)TaskbarPresentationState.FloatingFallback)]
    public void Starting_NonPlaceUsesAvailableFallbackWithoutEndingProcess(
        int decisionValue,
        bool floatingAvailable,
        int expectedStateValue)
    {
        TaskbarPresentationTransition transition = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.Starting,
            (PlacementDecision)decisionValue,
            floatingAvailable: floatingAvailable));

        AssertTransition(
            transition,
            (TaskbarPresentationState)expectedStateValue,
            floatingAvailable
                ? TaskbarPresentationSurface.Floating
                : TaskbarPresentationSurface.None);
    }

    [Fact]
    public void NativeVisible_StablePlaceKeepsNativeVisible()
    {
        TaskbarPresentationTransition transition = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.NativeVisible,
            PlacementDecision.Place));

        AssertTransition(
            transition,
            TaskbarPresentationState.NativeVisible,
            TaskbarPresentationSurface.Native);
    }

    [Fact]
    public void NativeVisible_TransientUnknownImmediatelyUsesFloatingFallback()
    {
        TaskbarPresentationTransition transition = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.NativeVisible,
            PlacementDecision.TransientUnknown));

        AssertTransition(
            transition,
            TaskbarPresentationState.FloatingFallback,
            TaskbarPresentationSurface.Floating);
    }

    [Fact]
    public void NativeVisible_TransientUnknownWithoutFloatingFailsClosedIntoHiddenRecovery()
    {
        TaskbarPresentationTransition transition = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.NativeVisible,
            PlacementDecision.TransientUnknown,
            floatingAvailable: false));

        AssertTransition(
            transition,
            TaskbarPresentationState.NativeRecoveryHidden,
            TaskbarPresentationSurface.None);
    }

    [Fact]
    public void NativeVisible_VerifiedNoFitImmediatelyUsesFallback()
    {
        TaskbarPresentationTransition transition = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.NativeVisible,
            PlacementDecision.VerifiedNoFit));

        AssertTransition(
            transition,
            TaskbarPresentationState.FloatingFallback,
            TaskbarPresentationSurface.Floating);
    }

    [Fact]
    public void NativeRecovery_TransientUnknownStaysHiddenUntilTimeoutThenFallsBack()
    {
        TaskbarPresentationTransition beforeTimeout = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.NativeRecoveryHidden,
            PlacementDecision.TransientUnknown,
            recoveryElapsed: TaskbarPresentationPolicy.MaximumNativeRecoveryDuration -
                TimeSpan.FromMilliseconds(1)));
        TaskbarPresentationTransition atTimeout = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.NativeRecoveryHidden,
            PlacementDecision.TransientUnknown,
            recoveryElapsed: TaskbarPresentationPolicy.MaximumNativeRecoveryDuration));

        AssertTransition(
            beforeTimeout,
            TaskbarPresentationState.NativeRecoveryHidden,
            TaskbarPresentationSurface.None);
        AssertTransition(
            atTimeout,
            TaskbarPresentationState.FloatingFallback,
            TaskbarPresentationSurface.Floating);
    }

    [Fact]
    public void NativeRecovery_PlaceStillNeedsPromotionAndCannotBeatTimeout()
    {
        TaskbarPresentationTransition waiting = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.NativeRecoveryHidden,
            PlacementDecision.Place,
            nativePromotionReady: false,
            recoveryElapsed: TimeSpan.FromSeconds(1)));
        TaskbarPresentationTransition ready = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.NativeRecoveryHidden,
            PlacementDecision.Place,
            nativePromotionReady: true,
            recoveryElapsed: TimeSpan.FromSeconds(1)));
        TaskbarPresentationTransition timedOut = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.NativeRecoveryHidden,
            PlacementDecision.Place,
            nativePromotionReady: true,
            recoveryElapsed: TaskbarPresentationPolicy.MaximumNativeRecoveryDuration));

        AssertTransition(
            waiting,
            TaskbarPresentationState.NativeRecoveryHidden,
            TaskbarPresentationSurface.None);
        AssertTransition(
            ready,
            TaskbarPresentationState.NativeVisible,
            TaskbarPresentationSurface.Native);
        AssertTransition(
            timedOut,
            TaskbarPresentationState.FloatingFallback,
            TaskbarPresentationSurface.Floating);
    }

    [Fact]
    public void NativeUnavailableOverridesReadyPlaceAndKeepsProcessAliveInFallback()
    {
        TaskbarPresentationTransition transition = TaskbarPresentationPolicy.Decide(CreateInput(
            TaskbarPresentationState.FloatingFallback,
            PlacementDecision.Place,
            nativeAvailable: false,
            nativePromotionReady: true));

        AssertTransition(
            transition,
            TaskbarPresentationState.FloatingFallback,
            TaskbarPresentationSurface.Floating);
    }

    [Fact]
    public void NegativeRecoveryElapsedIsRejected()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TaskbarPresentationPolicy.Decide(CreateInput(
                TaskbarPresentationState.NativeRecoveryHidden,
                PlacementDecision.TransientUnknown,
                recoveryElapsed: TimeSpan.FromTicks(-1))));
    }

    private static TaskbarPresentationInput CreateInput(
        TaskbarPresentationState state,
        PlacementDecision decision,
        bool invalidatedDuringScan = false,
        bool nativeAvailable = true,
        bool floatingAvailable = true,
        bool nativePromotionReady = false,
        TimeSpan? recoveryElapsed = null) =>
        new(
            state,
            decision,
            invalidatedDuringScan,
            nativeAvailable,
            floatingAvailable,
            nativePromotionReady,
            recoveryElapsed ?? TimeSpan.Zero);

    private static void AssertTransition(
        TaskbarPresentationTransition transition,
        TaskbarPresentationState expectedState,
        TaskbarPresentationSurface expectedSurface)
    {
        Assert.Equal(expectedState, transition.NextState);
        Assert.Equal(expectedSurface, transition.DesiredSurface);
        Assert.True(transition.ProcessShouldContinue);
    }
}
