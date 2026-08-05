using QuickPods.Spike.TaskbarHost.Diagnostics;
using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Placement;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class TaskbarLiveScanDiagnosticsTests
{
    [Fact]
    public void SignatureOutput_OmitsEphemeralIdentityAutomationIdsAndCoordinates()
    {
        TaskbarDiscoveryResult discovery = CreateDiscovery(
            ignoredHostMatch: true,
            faults:
            [
                new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.AutomationTimedOut),
            ]);
        var signature = TaskbarLiveScanSignature.Create(
            discovery,
            TaskbarPlacementResult.TransientUnknown(PlacementReason.IncompleteObservation),
            invalidatedDuringScan: true);
        List<string> output = [];
        using (var coalescer = new TaskbarLiveScanDiagnosticCoalescer(output.Add))
        {
            coalescer.Record(TaskbarLiveScanStage.Initial, signature);
        }

        string line = Assert.Single(output);
        Assert.Equal(
            "layout-scan=initial; discovery-complete=false; " +
            "decision=TransientUnknown; " +
            "reason=IncompleteObservation; faults=AutomationTimedOut; " +
            "buttons=1; critical=1; invalidated-during-scan=true; " +
            "ignored-host-match=true",
            line);
        Assert.DoesNotContain("305419896", line, StringComparison.Ordinal);
        Assert.DoesNotContain("424242", line, StringComparison.Ordinal);
        Assert.DoesNotContain("PotentiallySensitiveAutomationId", line, StringComparison.Ordinal);
        Assert.DoesNotContain("1729", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Coalescer_EmitsChangesAndSummarizesConsecutiveEqualRetries()
    {
        var incomplete = TaskbarLiveScanSignature.Create(
            new TaskbarDiscoveryResult(
                null,
                [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.AutomationTimedOut)]),
            TaskbarPlacementResult.TransientUnknown(PlacementReason.IncompleteObservation));
        var noFit = TaskbarLiveScanSignature.Create(
            CreateDiscovery(ignoredHostMatch: true),
            TaskbarPlacementResult.VerifiedNoFit(PlacementReason.InsufficientWidth));
        List<string> output = [];
        using (var coalescer = new TaskbarLiveScanDiagnosticCoalescer(output.Add))
        {
            coalescer.Record(TaskbarLiveScanStage.Recovery, incomplete);
            coalescer.Record(TaskbarLiveScanStage.Recovery, incomplete);
            coalescer.Record(TaskbarLiveScanStage.Recovery, incomplete);
            coalescer.Record(TaskbarLiveScanStage.Recovery, noFit);
            coalescer.Record(TaskbarLiveScanStage.Recovery, noFit);
            coalescer.Flush();
        }

        Assert.Collection(
            output,
            line => Assert.StartsWith(
                "layout-scan=recovery; discovery-complete=false; decision=TransientUnknown",
                line,
                StringComparison.Ordinal),
            line => Assert.StartsWith(
                "layout-scan-repeat=2; stage=recovery; discovery-complete=false; decision=TransientUnknown",
                line,
                StringComparison.Ordinal),
            line => Assert.StartsWith(
                "layout-scan=recovery; discovery-complete=true; decision=VerifiedNoFit",
                line,
                StringComparison.Ordinal),
            line => Assert.StartsWith(
                "layout-scan-repeat=1; stage=recovery; discovery-complete=true; decision=VerifiedNoFit",
                line,
                StringComparison.Ordinal));
    }

    [Fact]
    public void Flush_EmitsSuppressedRetriesBeforeTerminalOutcome()
    {
        var signature = TaskbarLiveScanSignature.Create(
            new TaskbarDiscoveryResult(
                null,
                [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.PrimaryTaskbarMissing)]),
            TaskbarPlacementResult.TransientUnknown(PlacementReason.IncompleteObservation));
        List<string> output = [];
        using (var coalescer = new TaskbarLiveScanDiagnosticCoalescer(output.Add))
        {
            coalescer.Record(TaskbarLiveScanStage.Recovery, signature);
            coalescer.Record(TaskbarLiveScanStage.Recovery, signature);
            coalescer.Flush();
            output.Add("layout-recovery=timeout");
        }

        Assert.Collection(
            output,
            line => Assert.StartsWith("layout-scan=recovery", line, StringComparison.Ordinal),
            line => Assert.StartsWith("layout-scan-repeat=1", line, StringComparison.Ordinal),
            line => Assert.Equal("layout-recovery=timeout", line));
    }

    private static TaskbarDiscoveryResult CreateDiscovery(
        bool ignoredHostMatch,
        IReadOnlyList<TaskbarDiscoveryFault>? faults = null)
    {
        var snapshot = new TaskbarSnapshot(
            taskbarHandle: 0x12345678,
            explorerProcessId: 424242,
            bounds: new PixelRect(100, 1680, 2000, 1760),
            dpi: 96,
            monitor: new TaskbarMonitorSnapshot(
                new PixelRect(100, 200, 2000, 1760),
                new PixelRect(100, 200, 2000, 1680),
                true),
            criticalChildren:
            [
                new CriticalTaskbarChildSnapshot(
                    CriticalTaskbarChildKind.NotificationArea,
                    new PixelRect(1700, 1680, 2000, 1760)),
            ],
            automationButtons:
            [
                new AutomationButtonSnapshot(
                    "PotentiallySensitiveAutomationId",
                    new PixelRect(1729, 1680, 1777, 1760),
                    true),
            ]);
        return new TaskbarDiscoveryResult(snapshot, faults ?? [], ignoredHostMatch);
    }
}
