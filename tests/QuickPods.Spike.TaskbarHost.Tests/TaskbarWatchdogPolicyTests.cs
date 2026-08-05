using QuickPods.Spike.TaskbarHost.Geometry;

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
    public void Decide_StableSameIdentityAndSafeCurrentBounds_KeepsVisible()
    {
        TaskbarWatchdogDecision decision = TaskbarWatchdogPolicy.Decide(CreateInput());

        Assert.Equal(TaskbarWatchdogDecision.KeepVisible, decision);
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
    public void Decide_InvalidatedDuringScan_EscalatesHiddenRecovery()
    {
        TaskbarWatchdogDecision decision = TaskbarWatchdogPolicy.Decide(
            CreateInput(invalidatedDuringScan: true));

        Assert.Equal(TaskbarWatchdogDecision.EscalateHiddenRecovery, decision);
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
}
