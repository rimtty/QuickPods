using QuickPods.Spike.TaskbarHost.Diagnostics;
using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class TaskbarLayoutAdapterTests
{
    private static readonly PixelRect TaskbarBounds = new(0, 1000, 1920, 1080);

    [Fact]
    public void CreateObservation_CompleteSnapshot_MapsLandmarksAndOnlyConcreteObstacles()
    {
        PixelRect start = new(900, 1000, 960, 1080);
        PixelRect widgets = new(0, 1000, 140, 1080);
        PixelRect ordinaryButton = new(500, 1000, 550, 1080);
        PixelRect notificationArea = new(1600, 1000, 1800, 1080);
        PixelRect clock = new(1800, 1000, 1920, 1080);
        PixelRect unknownNativeObstacle = new(600, 1000, 700, 1080);
        PixelRect structuralContainer = new(140, 1000, 1600, 1080);
        TaskbarDiscoveryResult discovery = CreateDiscovery(
            automationButtons:
            [
                new AutomationButtonSnapshot("StartButton", start, false),
                new AutomationButtonSnapshot("WidgetsButton", widgets, false),
                new AutomationButtonSnapshot(string.Empty, ordinaryButton, false),
            ],
            criticalChildren:
            [
                new CriticalTaskbarChildSnapshot(
                    CriticalTaskbarChildKind.NotificationArea,
                    notificationArea),
                new CriticalTaskbarChildSnapshot(CriticalTaskbarChildKind.Clock, clock),
                new CriticalTaskbarChildSnapshot(
                    CriticalTaskbarChildKind.UnknownObstacle,
                    unknownNativeObstacle),
                new CriticalTaskbarChildSnapshot(
                    CriticalTaskbarChildKind.TaskbarBand,
                    structuralContainer),
                new CriticalTaskbarChildSnapshot(
                    CriticalTaskbarChildKind.XamlIsland,
                    structuralContainer),
            ]);

        TaskbarLayoutObservation? observation = TaskbarLayoutAdapter.CreateObservation(discovery);

        Assert.NotNull(observation);
        Assert.True(observation.IsComplete);
        Assert.Equal(TaskbarOrientation.Horizontal, observation.Orientation);
        Assert.Equal(start, observation.StartButtonBounds);
        Assert.Equal(widgets, observation.WidgetsButtonBounds);
        Assert.Contains(ordinaryButton, observation.Obstacles);
        Assert.Contains(notificationArea, observation.Obstacles);
        Assert.Contains(clock, observation.Obstacles);
        Assert.Contains(unknownNativeObstacle, observation.Obstacles);
        Assert.DoesNotContain(structuralContainer, observation.Obstacles);
    }

    [Fact]
    public void CreateObservation_DiscoveryFault_MarksObservationIncomplete()
    {
        TaskbarDiscoveryResult discovery = CreateDiscovery(
            automationButtons:
            [
                new AutomationButtonSnapshot(
                    "StartButton",
                    new PixelRect(900, 1000, 960, 1080),
                    false),
            ],
            faults:
            [
                new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.AutomationPropertyUnavailable),
            ]);

        TaskbarLayoutObservation? observation = TaskbarLayoutAdapter.CreateObservation(discovery);

        Assert.NotNull(observation);
        Assert.False(observation.IsComplete);
    }

    [Fact]
    public void CreateObservation_DuplicateStartWithoutFaultStillFailsClosed()
    {
        PixelRect first = new(900, 1000, 960, 1080);
        PixelRect second = new(960, 1000, 1020, 1080);
        TaskbarDiscoveryResult discovery = CreateDiscovery(
            automationButtons:
            [
                new AutomationButtonSnapshot("StartButton", first, false),
                new AutomationButtonSnapshot("StartButton", second, false),
            ]);

        TaskbarLayoutObservation? observation = TaskbarLayoutAdapter.CreateObservation(discovery);

        Assert.NotNull(observation);
        Assert.False(observation.IsComplete);
        Assert.Null(observation.StartButtonBounds);
    }

    [Fact]
    public void CreateObservation_WithoutSnapshot_ReturnsNull()
    {
        var discovery = new TaskbarDiscoveryResult(
            null,
            [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.PrimaryTaskbarMissing)]);

        Assert.Null(TaskbarLayoutAdapter.CreateObservation(discovery));
    }

    private static TaskbarDiscoveryResult CreateDiscovery(
        IReadOnlyList<AutomationButtonSnapshot> automationButtons,
        IReadOnlyList<CriticalTaskbarChildSnapshot>? criticalChildren = null,
        IReadOnlyList<TaskbarDiscoveryFault>? faults = null)
    {
        var snapshot = new TaskbarSnapshot(
            taskbarHandle: 42,
            explorerProcessId: 84,
            bounds: TaskbarBounds,
            dpi: 96,
            monitor: new TaskbarMonitorSnapshot(
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 0, 1920, 1000),
                true),
            criticalChildren: criticalChildren ?? [],
            automationButtons: automationButtons);

        return new TaskbarDiscoveryResult(snapshot, faults ?? []);
    }
}
