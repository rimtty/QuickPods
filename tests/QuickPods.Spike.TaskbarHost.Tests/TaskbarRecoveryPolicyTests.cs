using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class TaskbarRecoveryPolicyTests
{
    private static readonly PixelRect TaskbarBounds = new(0, 1000, 1920, 1080);
    private static readonly PixelRect HostBounds = new(300, 1010, 600, 1070);
    private static readonly TaskbarHostIdentity CurrentIdentity = new(
        TaskbarHandle: 42,
        ExplorerProcessId: 84,
        Dpi: 144,
        TaskbarBounds);

    [Fact]
    public void Decide_NonPlaceBeforeDeadline_RetriesHidden()
    {
        TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(new(
            CurrentIdentity,
            HostBounds,
            DiscoveredIdentity: null,
            DiscoveredHostBounds: null,
            ForceRecreate: false,
            InvalidatedDuringScan: false,
            RecoveryElapsed: TimeSpan.FromSeconds(9)));

        Assert.Equal(TaskbarRecoveryDecision.RetryHidden, decision);
    }

    [Fact]
    public void Decide_AtRecoveryDeadline_FailsClosedEvenWhenPlaceWasFound()
    {
        TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(CreateInput(
            recoveryElapsed: TaskbarRecoveryPolicy.MaximumRecoveryDuration));

        Assert.Equal(TaskbarRecoveryDecision.FailClosed, decision);
    }

    [Fact]
    public void Decide_InvalidationRacingVerifiedScan_RetriesHidden()
    {
        TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(CreateInput(
            invalidatedDuringScan: true));

        Assert.Equal(TaskbarRecoveryDecision.RetryHidden, decision);
    }

    [Fact]
    public void Decide_TaskbarCreatedForcesRecreationForSameIdentity()
    {
        TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(CreateInput(
            forceRecreate: true));

        Assert.Equal(TaskbarRecoveryDecision.RecreateVerified, decision);
    }

    [Theory]
    [InlineData(43, 84, 144, 0, 1000, 1920, 1080)]
    [InlineData(42, 85, 144, 0, 1000, 1920, 1080)]
    [InlineData(42, 84, 192, 0, 1000, 1920, 1080)]
    [InlineData(42, 84, 144, 0, 960, 1920, 1040)]
    public void Decide_IdentityComponentChanged_Recreates(
        long taskbarHandle,
        uint explorerProcessId,
        uint dpi,
        int left,
        int top,
        int right,
        int bottom)
    {
        var changedIdentity = new TaskbarHostIdentity(
            new nint(taskbarHandle),
            explorerProcessId,
            dpi,
            new PixelRect(left, top, right, bottom));

        TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(CreateInput(
            discoveredIdentity: changedIdentity));

        Assert.Equal(TaskbarRecoveryDecision.RecreateVerified, decision);
    }

    [Fact]
    public void Decide_DifferentUnsafePlacement_Recreates()
    {
        PixelRect movedHostBounds = HostBounds with { Left = 350, Right = 650 };

        TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(CreateInput(
            discoveredHostBounds: movedHostBounds,
            currentBoundsRemainSafe: false));

        Assert.Equal(TaskbarRecoveryDecision.RecreateVerified, decision);
    }

    [Fact]
    public void Decide_DifferentPreferredPlacementShowsExistingWhenCurrentBoundsRemainSafe()
    {
        PixelRect movedPreferredBounds = HostBounds with { Left = 350, Right = 650 };

        TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(CreateInput(
            discoveredHostBounds: movedPreferredBounds,
            currentBoundsRemainSafe: true));

        Assert.Equal(TaskbarRecoveryDecision.ShowVerifiedExisting, decision);
    }

    [Fact]
    public void Decide_ForcedRecreationOverridesSafeDifferentPlacement()
    {
        PixelRect movedPreferredBounds = HostBounds with { Left = 350, Right = 650 };

        TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(CreateInput(
            discoveredHostBounds: movedPreferredBounds,
            forceRecreate: true,
            currentBoundsRemainSafe: true));

        Assert.Equal(TaskbarRecoveryDecision.RecreateVerified, decision);
    }

    [Fact]
    public void Decide_IdentityChangeOverridesSafeDifferentPlacement()
    {
        PixelRect movedPreferredBounds = HostBounds with { Left = 350, Right = 650 };
        TaskbarHostIdentity changedIdentity = CurrentIdentity with { ExplorerProcessId = 85 };

        TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(CreateInput(
            discoveredIdentity: changedIdentity,
            discoveredHostBounds: movedPreferredBounds,
            currentBoundsRemainSafe: true));

        Assert.Equal(TaskbarRecoveryDecision.RecreateVerified, decision);
    }

    [Fact]
    public void Decide_SameVerifiedIdentityAndPlacement_ShowsExisting()
    {
        TaskbarRecoveryDecision decision = TaskbarRecoveryPolicy.Decide(CreateInput());

        Assert.Equal(TaskbarRecoveryDecision.ShowVerifiedExisting, decision);
    }

    [Fact]
    public void RecoveryTiming_UsesBoundedQuarterSecondRetries()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), TaskbarRecoveryPolicy.RetryInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), TaskbarRecoveryPolicy.MaximumRecoveryDuration);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void IsSuccessfulDurationCompletion_RequiresStableNonFailedHost(
        bool recoveryInProgress,
        bool failedClosed,
        bool expected)
    {
        Assert.Equal(
            expected,
            TaskbarRecoveryPolicy.IsSuccessfulDurationCompletion(
                recoveryInProgress,
                failedClosed));
    }

    [Theory]
    [InlineData((int)NativeLayoutInvalidationReason.TaskbarCreated, true)]
    [InlineData((int)NativeLayoutInvalidationReason.DisplayChanged, true)]
    [InlineData((int)NativeLayoutInvalidationReason.DpiChanged, true)]
    [InlineData((int)NativeLayoutInvalidationReason.DpiChangedBeforeParent, true)]
    [InlineData((int)NativeLayoutInvalidationReason.DpiChangedAfterParent, true)]
    [InlineData((int)NativeLayoutInvalidationReason.SettingsChanged, false)]
    [InlineData((int)NativeLayoutInvalidationReason.ThemeChanged, false)]
    public void RequiresForcedRecreation_OnlyForShellOrCoordinateContextChanges(
        int reasonValue,
        bool expected)
    {
        Assert.Equal(
            expected,
            TaskbarRecoveryPolicy.RequiresForcedRecreation(
                (NativeLayoutInvalidationReason)reasonValue));
    }

    private static TaskbarRecoveryInput CreateInput(
        TaskbarHostIdentity? discoveredIdentity = null,
        PixelRect? discoveredHostBounds = null,
        bool forceRecreate = false,
        bool invalidatedDuringScan = false,
        bool currentBoundsRemainSafe = true,
        TimeSpan? recoveryElapsed = null)
    {
        return new(
            CurrentIdentity,
            HostBounds,
            discoveredIdentity ?? CurrentIdentity,
            discoveredHostBounds ?? HostBounds,
            forceRecreate,
            invalidatedDuringScan,
            recoveryElapsed ?? TimeSpan.FromSeconds(1),
            currentBoundsRemainSafe);
    }
}
