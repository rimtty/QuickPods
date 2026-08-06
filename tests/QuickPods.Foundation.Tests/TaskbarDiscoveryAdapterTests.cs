using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Placement;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class TaskbarDiscoveryAdapterTests
{
    [Fact]
    public void CompleteSnapshotMergesAutomationAndNativeObstacles()
    {
        TaskbarDiscoveryResult discovery = Result(
            buttons:
            [
                new AutomationButtonSnapshot("StartButton", new PixelRect(900, 0, 945, 48)),
                new AutomationButtonSnapshot("Pinned", new PixelRect(600, 0, 648, 48)),
            ],
            nativeObstacles:
            [
                new NativeTaskbarObstacle(NativeObstacleKind.NotificationArea, new PixelRect(1600, 0, 1920, 48)),
            ]);

        TaskbarLayoutObservation? observation = TaskbarObservationAdapter.Create(discovery);

        Assert.NotNull(observation);
        Assert.True(observation.IsComplete);
        Assert.Equal(TaskbarOrientation.Horizontal, observation.Orientation);
        Assert.Equal(3, observation.Obstacles.Count);
    }

    [Fact]
    public void DuplicateStartCannotBecomeACompleteObservation()
    {
        TaskbarDiscoveryResult discovery = Result(
            buttons:
            [
                new AutomationButtonSnapshot("StartButton", new PixelRect(900, 0, 945, 48)),
                new AutomationButtonSnapshot("StartButton", new PixelRect(950, 0, 995, 48)),
            ],
            faults: [TaskbarDiscoveryFault.StartButtonDuplicate]);

        TaskbarLayoutObservation? observation = TaskbarObservationAdapter.Create(discovery);

        Assert.NotNull(observation);
        Assert.False(observation.IsComplete);
        Assert.Null(observation.StartButtonBounds);
        Assert.Equal(
            PlacementDecision.TransientUnknown,
            SafeRegionPlanner.Calculate(observation, TaskbarPlacementOptions.Default).Decision);
    }

    private static TaskbarDiscoveryResult Result(
        IReadOnlyList<AutomationButtonSnapshot> buttons,
        IReadOnlyList<NativeTaskbarObstacle>? nativeObstacles = null,
        IReadOnlyList<TaskbarDiscoveryFault>? faults = null)
    {
        var snapshot = new LiveTaskbarSnapshot(
            taskbarHandle: (nint)1,
            explorerProcessId: 2,
            bounds: new PixelRect(0, 0, 1920, 48),
            dpi: 96,
            monitorBounds: new PixelRect(0, 0, 1920, 1080),
            workArea: new PixelRect(0, 0, 1920, 1032),
            nativeObstacles: nativeObstacles ?? [],
            automationButtons: buttons);
        return new TaskbarDiscoveryResult(snapshot, faults ?? [], IgnoredHostMatched: false);
    }
}
