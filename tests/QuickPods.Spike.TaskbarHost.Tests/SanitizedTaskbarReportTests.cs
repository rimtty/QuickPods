using System.Text.Json;
using QuickPods.Spike.TaskbarHost.Diagnostics;
using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class SanitizedTaskbarReportTests
{
    [Fact]
    public void ToJson_OmitsEphemeralHandlesPidAndNonLandmarkAutomationIds()
    {
        var taskbarBounds = new PixelRect(-1920, 1032, 0, 1080);
        var snapshot = new TaskbarSnapshot(
            taskbarHandle: 0x12345678,
            explorerProcessId: 424242,
            bounds: taskbarBounds,
            dpi: 96,
            monitor: new TaskbarMonitorSnapshot(
                new PixelRect(-1920, 0, 0, 1080),
                new PixelRect(-1920, 0, 0, 1032),
                true),
            criticalChildren:
            [
                new CriticalTaskbarChildSnapshot(
                    CriticalTaskbarChildKind.NotificationArea,
                    new PixelRect(-300, 1032, 0, 1080)),
            ],
            automationButtons:
            [
                new AutomationButtonSnapshot(
                    "StartButton",
                    new PixelRect(-900, 1032, -852, 1080),
                    false),
                new AutomationButtonSnapshot(
                    "WidgetsButton",
                    new PixelRect(-1920, 1032, -1800, 1080),
                    false),
                new AutomationButtonSnapshot(
                    "PotentiallySensitiveAppIdentifier",
                    new PixelRect(-800, 1032, -752, 1080),
                    false),
            ]);
        var discovery = new TaskbarDiscoveryResult(snapshot, []);
        var placement = TaskbarPlacementResult.Place(
            new PixelRect(-1500, 1036, -1200, 1076),
            TaskbarStripMode.Standard);

        string json = SanitizedTaskbarReport.Create(
            discovery,
            placement,
            TimeSpan.FromMilliseconds(12.3456)).ToJson();

        Assert.DoesNotContain("305419896", json, StringComparison.Ordinal);
        Assert.DoesNotContain("424242", json, StringComparison.Ordinal);
        Assert.DoesNotContain("taskbarHandle", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("explorerProcessId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PotentiallySensitiveAppIdentifier", json, StringComparison.Ordinal);
        Assert.Contains("StartButton", json, StringComparison.Ordinal);
        Assert.Contains("WidgetsButton", json, StringComparison.Ordinal);
        Assert.Contains("\"x\": 1020", json, StringComparison.Ordinal);

        string directSnapshotJson = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("PotentiallySensitiveAppIdentifier", directSnapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("automationButtons", directSnapshotJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_WithoutSnapshot_ReportsFaultAndUnknownPlacement()
    {
        var discovery = new TaskbarDiscoveryResult(
            null,
            [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.PrimaryTaskbarMissing)]);
        var placement = TaskbarPlacementResult.TransientUnknown(
            PlacementReason.IncompleteObservation);

        var report = SanitizedTaskbarReport.Create(
            discovery,
            placement,
            TimeSpan.Zero);

        Assert.False(report.DiscoveryComplete);
        Assert.Null(report.Taskbar);
        Assert.Contains(nameof(TaskbarDiscoveryFaultCode.PrimaryTaskbarMissing), report.Faults);
        Assert.Equal(PlacementDecision.TransientUnknown, report.Placement.Decision);
    }
}
