using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class TaskbarWatchdogPolicyTests
{
    private static readonly PixelRect TaskbarBounds = new(0, 1000, 1920, 1080);
    private static readonly PixelRect CurrentBounds = new(300, 1010, 750, 1070);
    private static readonly TaskbarHostIdentity CurrentIdentity = new(
        TaskbarHandle: 42,
        ExplorerProcessId: 84,
        Dpi: 144,
        TaskbarBounds);

    [Fact]
    public void Decide_StableAndOrderedContinuityScenarios_AreFailClosed()
    {
        TaskbarWatchdogDecision decision = TaskbarWatchdogPolicy.Decide(CreateInput());

        Assert.Equal(TaskbarWatchdogDecision.KeepVisible, decision);

        TaskbarLayoutObservation fresh = CreateObservation(
            new PixelRect(800, 1010, 850, 1070));
        TaskbarLayoutObservation priorUnsafe = CreateObservation(
            new PixelRect(400, 1010, 500, 1070));
        TaskbarContinuityAttemptDecision retainedDecision =
            TaskbarContinuityAttemptPolicy.Decide(CreateAttemptInput(
                new TaskbarContinuityEvidence
                {
                    RetainedNotificationAreaContinuity = true,
                    AutomationOrigin =
                        TaskbarAutomationContinuityOrigin.RetainedStartFromCompleteAnchor,
                },
                priorUnsafe,
                fresh));
        TaskbarContinuityAttemptDecision newObstacleDecision =
            TaskbarContinuityAttemptPolicy.Decide(CreateAttemptInput(
                new TaskbarContinuityEvidence
                {
                    AutomationOrigin = TaskbarAutomationContinuityOrigin.FreshComplete,
                },
                fresh,
                priorUnsafe));
        TaskbarContinuityAttemptDecision freshCompleteDecision =
            TaskbarContinuityAttemptPolicy.Decide(CreateAttemptInput(
                new TaskbarContinuityEvidence
                {
                    AutomationOrigin = TaskbarAutomationContinuityOrigin.FreshComplete,
                },
                priorUnsafe,
                fresh));

        Assert.Equal(
            TaskbarWatchdogDecision.EscalateHiddenRecovery,
            retainedDecision.WatchdogDecision);
        Assert.Contains(priorUnsafe.Obstacles[0], retainedDecision.SafetyObservation!.Obstacles);
        Assert.Equal(
            TaskbarWatchdogDecision.EscalateHiddenRecovery,
            newObstacleDecision.WatchdogDecision);
        Assert.Equal(
            TaskbarWatchdogDecision.KeepVisible,
            freshCompleteDecision.WatchdogDecision);
        Assert.DoesNotContain(
            priorUnsafe.Obstacles[0],
            freshCompleteDecision.SafetyObservation!.Obstacles);
    }

    [Fact]
    public void Decide_DifferentPreferredBoundsButSafeStickyBounds_KeepsVisible()
    {
        PixelRect differentPreferredBounds = CurrentBounds with
        {
            Left = 800,
            Right = 1250,
        };

        TaskbarWatchdogDecision decision = TaskbarWatchdogPolicy.Decide(
            CreateInput(discoveredBounds: differentPreferredBounds));

        Assert.Equal(TaskbarWatchdogDecision.KeepVisible, decision);
    }

    [Fact]
    public void Decide_MissingVerifiedObservation_EscalatesHiddenRecovery()
    {
        TaskbarWatchdogDecision decision = TaskbarWatchdogPolicy.Decide(
            CreateInput(hasObservation: false));

        Assert.Equal(TaskbarWatchdogDecision.EscalateHiddenRecovery, decision);
    }

    [Fact]
    public void Decide_InFlightGenerationAndCoordinateInvalidations_HideImmediately()
    {
        TaskbarWatchdogDecision decision = TaskbarWatchdogPolicy.Decide(
            CreateInput(invalidatedDuringScan: true));

        Assert.Equal(TaskbarWatchdogDecision.EscalateHiddenRecovery, decision);

        TaskbarContinuityAttemptInput stableInput = CreateAttemptInput(
            new TaskbarContinuityEvidence
            {
                AutomationOrigin = TaskbarAutomationContinuityOrigin.FreshComplete,
            },
            CreateObservation(new PixelRect(800, 1010, 850, 1070)),
            CreateObservation(new PixelRect(800, 1010, 850, 1070)));
        TaskbarContinuityAttemptDecision racedDecision =
            TaskbarContinuityAttemptPolicy.Decide(stableInput with
            {
                Completion = stableInput.Completion with
                {
                    AutomationGeneration =
                        stableInput.Completion.AutomationGeneration + 1,
                },
            });

        Assert.True(racedDecision.InvalidatedDuringScan);
        Assert.Equal(
            TaskbarWatchdogDecision.EscalateHiddenRecovery,
            racedDecision.WatchdogDecision);
        foreach (NativeLayoutInvalidationReason reason in new[]
        {
            NativeLayoutInvalidationReason.SettingsChanged,
            NativeLayoutInvalidationReason.DisplayChanged,
            NativeLayoutInvalidationReason.DpiChanged,
        })
        {
            Assert.True(TaskbarContinuityAttemptPolicy.RequiresImmediateHide(new(
                reason,
                NativeLayoutInvalidated: true,
                AutomationLayoutInvalidated: false,
                FloatingLayoutInvalidated: false)));
        }
    }

    [Fact]
    public void Decide_ChangedIdentity_EscalatesHiddenRecovery()
    {
        TaskbarHostIdentity changedIdentity = CurrentIdentity with { ExplorerProcessId = 85 };

        TaskbarWatchdogDecision decision = TaskbarWatchdogPolicy.Decide(
            CreateInput(discoveredIdentity: changedIdentity));

        Assert.Equal(TaskbarWatchdogDecision.EscalateHiddenRecovery, decision);
    }

    [Fact]
    public void Decide_UnsafeCurrentBounds_EscalatesHiddenRecovery()
    {
        TaskbarWatchdogDecision decision = TaskbarWatchdogPolicy.Decide(
            CreateInput(currentBoundsRemainSafe: false));

        Assert.Equal(TaskbarWatchdogDecision.EscalateHiddenRecovery, decision);
    }

    [Fact]
    public void Decide_InvalidNativeAttachment_EscalatesHiddenRecovery()
    {
        TaskbarWatchdogDecision decision = TaskbarWatchdogPolicy.Decide(
            CreateInput(nativeAttachmentIsValid: false));

        Assert.Equal(TaskbarWatchdogDecision.EscalateHiddenRecovery, decision);
    }

    private static TaskbarWatchdogInput CreateInput(
        bool hasObservation = true,
        TaskbarHostIdentity? discoveredIdentity = null,
        PixelRect? discoveredBounds = null,
        bool invalidatedDuringScan = false,
        bool currentBoundsRemainSafe = true,
        bool nativeAttachmentIsValid = true) =>
        new(
            CurrentIdentity,
            CurrentBounds,
            hasObservation ? discoveredIdentity ?? CurrentIdentity : null,
            hasObservation ? discoveredBounds ?? CurrentBounds : null,
            invalidatedDuringScan,
            currentBoundsRemainSafe,
            nativeAttachmentIsValid);

    private static TaskbarContinuityAttemptInput CreateAttemptInput(
        TaskbarContinuityEvidence evidence,
        TaskbarLayoutObservation previous,
        TaskbarLayoutObservation fresh)
    {
        var fence = new TaskbarContinuityScanFence(
            AutomationGeneration: 7,
            InvalidationGeneration: 11);
        var completion = new TaskbarContinuityScanCompletion(
            AutomationGeneration: 7,
            InvalidationGeneration: 11,
            NativeLayoutInvalidated: false,
            AutomationLayoutInvalidated: false,
            FloatingLayoutInvalidated: false,
            ContinuityProbeRequested: false);
        return new(
            fence,
            completion,
            DiscoveryVerified: true,
            evidence,
            previous,
            fresh,
            CurrentIdentity,
            CurrentBounds,
            CurrentIdentity,
            CurrentBounds,
            NativeAttachmentValid: true);
    }

    private static TaskbarLayoutObservation CreateObservation(PixelRect obstacle) =>
        new(
            TaskbarBounds,
            Dpi: 144,
            TaskbarOrientation.Horizontal,
            StartButtonBounds: new PixelRect(900, 1000, 960, 1080),
            WidgetsButtonBounds: new PixelRect(0, 1000, 140, 1080),
            Obstacles: [obstacle],
            IsComplete: true);
}
