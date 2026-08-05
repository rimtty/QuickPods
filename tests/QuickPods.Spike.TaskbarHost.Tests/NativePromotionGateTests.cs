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
    public void Observe_A_SingleEligiblePlaceNeverPromotes()
    {
        var gate = new NativePromotionGate(TimeSpan.Zero);

        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromSeconds(1))));
        Assert.True(gate.HasCandidate);
    }

    [Theory]
    [InlineData((int)PlacementDecision.VerifiedNoFit, false, false)]
    [InlineData((int)PlacementDecision.TransientUnknown, false, false)]
    [InlineData((int)PlacementDecision.Place, true, false)]
    [InlineData((int)PlacementDecision.Place, false, true)]
    public void Observe_NonPlaceRaceOrInvalidationResetsCandidate(
        int decisionValue,
        bool invalidatedDuringScan,
        bool layoutInvalidated)
    {
        var gate = new NativePromotionGate(TimeSpan.Zero);
        Assert.False(gate.Observe(PlaceAt(TimeSpan.FromSeconds(1))));

        Assert.False(gate.Observe(new NativePromotionObservation(
            (PlacementDecision)decisionValue,
            Candidate,
            invalidatedDuringScan,
            layoutInvalidated,
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

    [Theory]
    [InlineData(43, 84, 144, (int)TaskbarStripMode.Standard)]
    [InlineData(42, 85, 144, (int)TaskbarStripMode.Standard)]
    [InlineData(42, 84, 192, (int)TaskbarStripMode.Standard)]
    [InlineData(42, 84, 144, (int)TaskbarStripMode.Compact)]
    public void Observe_IdentityOrModeChangeResetsCandidate(
        long taskbarHandle,
        uint explorerProcessId,
        uint dpi,
        int modeValue)
    {
        var gate = new NativePromotionGate(TimeSpan.Zero);
        var changedIdentity = new TaskbarHostIdentity(
            new nint(taskbarHandle),
            explorerProcessId,
            dpi,
            TaskbarBounds);
        var changed = new NativePromotionCandidate(
            changedIdentity,
            HostBounds,
            (TaskbarStripMode)modeValue);

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

    [Fact]
    public void CandidateStringDoesNotExposeRawTaskbarIdentity()
    {
        var sensitiveIdentity = new TaskbarHostIdentity(
            TaskbarHandle: 0x12345678,
            ExplorerProcessId: 424242,
            Dpi: 144,
            TaskbarBounds);
        var candidate = new NativePromotionCandidate(
            sensitiveIdentity,
            HostBounds,
            TaskbarStripMode.Standard);

        string text = candidate.ToString();

        Assert.DoesNotContain("305419896", text, StringComparison.Ordinal);
        Assert.DoesNotContain("424242", text, StringComparison.Ordinal);
        Assert.Contains(nameof(TaskbarStripMode.Standard), text, StringComparison.Ordinal);
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
