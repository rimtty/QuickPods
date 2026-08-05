using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Placement;
using QuickPods.Spike.TaskbarHost.Presentation;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class NativePromotionGateTests
{
    private static readonly PixelRect TaskbarBounds = new(0, 1000, 1920, 1080);
    private static readonly PixelRect HostBounds = new(300, 1010, 600, 1070);
    private static readonly TaskbarHostIdentity Identity = new(
        TaskbarHandle: 42,
        ExplorerProcessId: 84,
        Dpi: 144,
        TaskbarBounds);
    private static readonly NativePromotionCandidate Candidate = new(
        Identity,
        HostBounds,
        TaskbarStripMode.Standard);

    [Fact]
    public void Observe_RequiresCooldownThenTwoIdenticalSamplesAtLeast500MillisecondsApart()
    {
        var gate = new NativePromotionGate(TimeSpan.Zero);

        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(999))));
        Assert.False(gate.HasCandidate);
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromSeconds(1))));
        Assert.True(gate.HasCandidate);
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(1499))));
        Assert.True(gate.HasCandidate);
        Assert.True(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(1500))));
        Assert.False(gate.HasCandidate);
    }

    [Fact]
    public void Observe_InvalidatedScanResetsCandidate()
    {
        var gate = new NativePromotionGate(TimeSpan.Zero);
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromSeconds(1))));

        Assert.False(gate.Observe(new NativePromotionObservation(
            PlacementDecision.Place,
            Candidate,
            InvalidatedDuringScan: true,
            LayoutInvalidated: false,
            TimeSpan.FromMilliseconds(1600))));
        Assert.False(gate.HasCandidate);
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(2200))));
        Assert.True(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(2700))));
    }

    [Fact]
    public void Observe_CandidateChangeStartsANewConfirmationPair()
    {
        var gate = new NativePromotionGate(TimeSpan.Zero);
        NativePromotionCandidate changed = Candidate with
        {
            Bounds = HostBounds with { Left = 350, Right = 650 },
        };

        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromSeconds(1))));
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(1600), changed)));
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(2099), changed)));
        Assert.True(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(2100), changed)));
    }

    [Fact]
    public void Observe_TaskbarIdentityChangeResetsCandidate()
    {
        var gate = new NativePromotionGate(TimeSpan.Zero);
        var changedIdentity = new TaskbarHostIdentity(
            new nint(43),
            Identity.ExplorerProcessId,
            Identity.Dpi,
            TaskbarBounds);
        var changed = new NativePromotionCandidate(
            changedIdentity,
            HostBounds,
            TaskbarStripMode.Standard);

        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromSeconds(1))));
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(1600), changed)));
        Assert.True(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(2100), changed)));
    }

    [Fact]
    public void BeginModeSwitchRestartsCooldownAndClearsCandidate()
    {
        var gate = new NativePromotionGate(TimeSpan.Zero);
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromSeconds(1))));

        gate.BeginModeSwitch(TimeSpan.FromMilliseconds(1200));

        Assert.False(gate.HasCandidate);
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(2199))));
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(2200))));
        Assert.True(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(2700))));
    }

    [Fact]
    public void Observe_InvalidCandidateFailsClosedAndResetsPriorCandidate()
    {
        var gate = new NativePromotionGate(TimeSpan.Zero);
        NativePromotionCandidate invalid = Candidate with
        {
            TaskbarIdentity = Identity with { TaskbarHandle = nint.Zero },
        };
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromSeconds(1))));

        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(1600), invalid)));

        Assert.False(gate.HasCandidate);
    }

    [Fact]
    public void ElapsedInputsMustBeMonotonic()
    {
        var gate = new NativePromotionGate(TimeSpan.FromSeconds(1));

        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            gate.Observe(PlaceAt(TimeSpan.FromMilliseconds(999))));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() =>
            gate.BeginModeSwitch(TimeSpan.FromMilliseconds(999)));
    }

    private static NativePromotionObservation PlaceAt(
        TimeSpan elapsed,
        NativePromotionCandidate? candidate = null) =>
        new(
            PlacementDecision.Place,
            candidate ?? Candidate,
            InvalidatedDuringScan: false,
            LayoutInvalidated: false,
            elapsed);
}
