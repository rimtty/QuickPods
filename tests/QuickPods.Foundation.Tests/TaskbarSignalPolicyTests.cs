using QuickPods.Contracts;
using QuickPods.TaskbarObserver;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class TaskbarSignalPolicyTests
{
    private const uint TaskbarCreatedMessage = 0xC0A1;

    [Theory]
    [InlineData(1UL, false)]
    [InlineData(2UL, false)]
    [InlineData(13UL, false)]
    [InlineData(1UL, true)]
    [InlineData(2UL, true)]
    [InlineData(13UL, true)]
    public void TaskbarListChangesFromTheShellHookInvalidateStructure(ulong code, bool labelsVisible)
    {
        SignalDisposition disposition = TaskbarSignalPolicy.ClassifyShellHook(code, labelsVisible);

        Assert.Equal(ObserverInvalidationKind.StructureChanged, disposition.Kind);
        Assert.Equal(ObserverSourceClassification.External, disposition.Source);
        Assert.True(disposition.ArmSettle);
        Assert.False(TaskbarSignalPolicy.IsThrottledShellHook(code));
    }

    [Fact]
    public void ShellRedrawInvalidatesStructureOnlyWhileLabelsAreVisibleAndIsThrottled()
    {
        SignalDisposition withLabels = TaskbarSignalPolicy.ClassifyShellHook(6UL, taskbarLabelsVisible: true);
        SignalDisposition withoutLabels = TaskbarSignalPolicy.ClassifyShellHook(6UL, taskbarLabelsVisible: false);

        Assert.Equal(ObserverInvalidationKind.StructureChanged, withLabels.Kind);
        Assert.True(withoutLabels.IsNone);
        Assert.True(TaskbarSignalPolicy.IsThrottledShellHook(6UL));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData("2", false)]
    public void TaskbarLabelsAreVisibleOnlyForANonZeroGlomLevel(object? glomLevel, bool expected)
    {
        Assert.Equal(expected, TaskbarSignalPolicy.AreTaskbarLabelsVisible(glomLevel));
    }

    [Theory]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(14UL)]
    [InlineData(16UL)]
    [InlineData(0x8004UL)]
    [InlineData(0x8006UL)]
    public void ActivationFlashAndUnrelatedShellHooksAreIgnored(ulong code)
    {
        Assert.True(TaskbarSignalPolicy.ClassifyShellHook(code, taskbarLabelsVisible: true).IsNone);
        Assert.True(TaskbarSignalPolicy.ClassifyShellHook(code, taskbarLabelsVisible: false).IsNone);
    }

    [Theory]
    [InlineData(0x8000u, ObserverInvalidationKind.StructureChanged)]
    [InlineData(0x8001u, ObserverInvalidationKind.StructureChanged)]
    [InlineData(0x8004u, ObserverInvalidationKind.StructureChanged)]
    [InlineData(0x8002u, ObserverInvalidationKind.IsOffscreenChanged)]
    [InlineData(0x8003u, ObserverInvalidationKind.IsOffscreenChanged)]
    public void TaskbarTreeWindowEventsInvalidateImmediatelyAndArmSettle(
        uint eventId,
        ObserverInvalidationKind expected)
    {
        SignalDisposition disposition = TaskbarSignalPolicy.ClassifyWinEvent(eventId, 0, 0, true);

        Assert.Equal(expected, disposition.Kind);
        Assert.Equal(ObserverSourceClassification.External, disposition.Source);
        Assert.True(disposition.ArmSettle);
    }

    [Fact]
    public void LocationChangesOnlyArmTheSettleTimer()
    {
        SignalDisposition disposition = TaskbarSignalPolicy.ClassifyWinEvent(0x800Bu, 0, 0, true);

        Assert.Null(disposition.Kind);
        Assert.True(disposition.ArmSettle);
        Assert.False(disposition.IsNone);
    }

    [Theory]
    [InlineData(0x800Bu, -9, 0, true)]
    [InlineData(0x800Bu, 0, 3, true)]
    [InlineData(0x8000u, 0, 0, false)]
    [InlineData(0x8005u, 0, 0, true)]
    [InlineData(0x800Cu, 0, 0, true)]
    public void NonWindowChildOutsideTreeAndUnknownEventsAreIgnored(
        uint eventId,
        int objectId,
        int childId,
        bool isInTaskbarTree)
    {
        Assert.True(
            TaskbarSignalPolicy.ClassifyWinEvent(eventId, objectId, childId, isInTaskbarTree).IsNone);
    }

    [Theory]
    [InlineData(0x001Au)]
    [InlineData(0x007Eu)]
    [InlineData(0x031Au)]
    [InlineData(TaskbarCreatedMessage)]
    public void ShellBroadcastsInvalidateStructureWithoutIdentity(uint message)
    {
        SignalDisposition disposition =
            TaskbarSignalPolicy.ClassifyWindowMessage(message, TaskbarCreatedMessage);

        Assert.Equal(ObserverInvalidationKind.StructureChanged, disposition.Kind);
        Assert.Equal(ObserverSourceClassification.Unknown, disposition.Source);
        Assert.True(disposition.ArmSettle);
    }

    [Theory]
    [InlineData(0x0113u)]
    [InlineData(0x000Fu)]
    [InlineData(TaskbarCreatedMessage)]
    public void UnrelatedWindowMessagesAreIgnored(uint message)
    {
        Assert.True(TaskbarSignalPolicy.ClassifyWindowMessage(message, 0).IsNone);
    }

    [Fact]
    public void RegistryAndSettleSignalsAreUnknownSourced()
    {
        SignalDisposition registry = TaskbarSignalPolicy.ClassifyRegistryChange();
        SignalDisposition settle = TaskbarSignalPolicy.Settle;

        Assert.Equal(ObserverInvalidationKind.StructureChanged, registry.Kind);
        Assert.Equal(ObserverSourceClassification.Unknown, registry.Source);
        Assert.True(registry.ArmSettle);
        Assert.Equal(ObserverInvalidationKind.BoundingRectangleChanged, settle.Kind);
        Assert.Equal(ObserverSourceClassification.Unknown, settle.Source);
        Assert.False(settle.ArmSettle);
    }

    [Fact]
    public void ThrottleEmitsLeadingEdgeThenTrailsUntilTheWindowElapses()
    {
        TimeSpan window = TimeSpan.FromSeconds(1);
        TimeSpan start = TimeSpan.FromSeconds(10);

        Assert.True(TaskbarSignalPolicy.ShouldEmitThrottled(start, null, window, out bool trailing));
        Assert.False(trailing);

        Assert.False(TaskbarSignalPolicy.ShouldEmitThrottled(
            start + TimeSpan.FromMilliseconds(300),
            start,
            window,
            out trailing));
        Assert.True(trailing);

        Assert.True(TaskbarSignalPolicy.ShouldEmitThrottled(start + window, start, window, out trailing));
        Assert.False(trailing);
    }

    [Fact]
    public void NoDispositionEverClaimsAnOwnedSource()
    {
        var dispositions = new List<SignalDisposition>
        {
            TaskbarSignalPolicy.ClassifyRegistryChange(),
            TaskbarSignalPolicy.Settle,
            TaskbarSignalPolicy.ClassifyWindowMessage(0x001Au, TaskbarCreatedMessage),
        };
        for (ulong code = 0; code <= 0x8010UL; code++)
        {
            dispositions.Add(TaskbarSignalPolicy.ClassifyShellHook(code, taskbarLabelsVisible: true));
            dispositions.Add(TaskbarSignalPolicy.ClassifyShellHook(code, taskbarLabelsVisible: false));
        }

        for (uint eventId = 0x8000u; eventId <= 0x8010u; eventId++)
        {
            dispositions.Add(TaskbarSignalPolicy.ClassifyWinEvent(eventId, 0, 0, true));
        }

        Assert.DoesNotContain(
            dispositions,
            disposition => disposition.Source == ObserverSourceClassification.Owned);
    }
}
