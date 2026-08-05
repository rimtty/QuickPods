using QuickPods.Spike.TaskbarHost.Hosting;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class NativeDpiAwarenessProbeTests
{
    [Theory]
    [InlineData((int)NativeDpiAwareness.Unaware, 96u, false)]
    [InlineData((int)NativeDpiAwareness.PerMonitorAware, 144u, true)]
    public void IsStableAfterParenting_UnchangedKnownContextAndParentDpi_ReturnsTrue(
        int awarenessValue,
        uint dpi,
        bool isPerMonitorV2)
    {
        var awareness = (NativeDpiAwareness)awarenessValue;
        var measurement = new NativeDpiAwarenessMeasurement(
            awareness,
            awareness,
            awareness,
            isPerMonitorV2,
            isPerMonitorV2,
            dpi);

        Assert.True(NativeDpiAwarenessProbe.IsStableAfterParenting(
            measurement,
            measurement,
            dpi));
    }

    [Fact]
    public void IsStableAfterParenting_ChangedContext_ReturnsFalse()
    {
        var before = new NativeDpiAwarenessMeasurement(
            NativeDpiAwareness.PerMonitorAware,
            NativeDpiAwareness.PerMonitorAware,
            NativeDpiAwareness.PerMonitorAware,
            true,
            true,
            144);
        NativeDpiAwarenessMeasurement after = before with { Window = NativeDpiAwareness.SystemAware };

        Assert.False(NativeDpiAwarenessProbe.IsStableAfterParenting(before, after, 144));
    }

    [Fact]
    public void IsStableAfterParenting_ParentDpiMismatch_ReturnsFalse()
    {
        var measurement = new NativeDpiAwarenessMeasurement(
            NativeDpiAwareness.PerMonitorAware,
            NativeDpiAwareness.PerMonitorAware,
            NativeDpiAwareness.PerMonitorAware,
            true,
            true,
            144);

        Assert.False(NativeDpiAwarenessProbe.IsStableAfterParenting(
            measurement,
            measurement,
            192));
    }

    [Fact]
    public void IsStableAfterParenting_UnknownContext_ReturnsFalse()
    {
        var measurement = new NativeDpiAwarenessMeasurement(
            NativeDpiAwareness.Unknown,
            NativeDpiAwareness.PerMonitorAware,
            NativeDpiAwareness.PerMonitorAware,
            true,
            true,
            144);

        Assert.False(NativeDpiAwarenessProbe.IsStableAfterParenting(
            measurement,
            measurement,
            144));
    }

    [Fact]
    public void IsStableAfterParenting_PerMonitorV2Downgrade_ReturnsFalse()
    {
        var before = new NativeDpiAwarenessMeasurement(
            NativeDpiAwareness.PerMonitorAware,
            NativeDpiAwareness.PerMonitorAware,
            NativeDpiAwareness.PerMonitorAware,
            true,
            true,
            144);
        NativeDpiAwarenessMeasurement after = before with { WindowIsPerMonitorV2 = false };

        Assert.False(NativeDpiAwarenessProbe.IsStableAfterParenting(before, after, 144));
    }

    [Fact]
    public void IsPerMonitorV2StableAfterParenting_FullyVerifiedContext_ReturnsTrue()
    {
        var measurement = new NativeDpiAwarenessMeasurement(
            NativeDpiAwareness.PerMonitorAware,
            NativeDpiAwareness.PerMonitorAware,
            NativeDpiAwareness.PerMonitorAware,
            true,
            true,
            144);

        Assert.True(NativeDpiAwarenessProbe.IsPerMonitorV2StableAfterParenting(
            measurement,
            measurement,
            144));
    }

    [Theory]
    [InlineData((int)NativeDpiAwareness.SystemAware, true, true, 144u)]
    [InlineData((int)NativeDpiAwareness.PerMonitorAware, false, true, 144u)]
    [InlineData((int)NativeDpiAwareness.PerMonitorAware, true, false, 144u)]
    [InlineData((int)NativeDpiAwareness.PerMonitorAware, true, true, 120u)]
    public void IsPerMonitorV2StableAfterParenting_NonPmv2OrVirtualizedContext_ReturnsFalse(
        int awarenessValue,
        bool threadIsPerMonitorV2,
        bool windowIsPerMonitorV2,
        uint beforeDpi)
    {
        var awareness = (NativeDpiAwareness)awarenessValue;
        var before = new NativeDpiAwarenessMeasurement(
            awareness,
            awareness,
            awareness,
            threadIsPerMonitorV2,
            windowIsPerMonitorV2,
            beforeDpi);
        NativeDpiAwarenessMeasurement after = before with { WindowDpi = 144 };

        Assert.False(NativeDpiAwarenessProbe.IsPerMonitorV2StableAfterParenting(
            before,
            after,
            144));
    }
}
