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
    public void Signatures_DistinguishIncompleteNoFitAndInvalidationRace()
    {
        var incompleteDiscovery = new TaskbarDiscoveryResult(
            null,
            [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.PrimaryTaskbarMissing)]);
        var incomplete = TaskbarLiveScanSignature.Create(
            incompleteDiscovery,
            TaskbarPlacementResult.TransientUnknown(PlacementReason.IncompleteObservation));
        var noFit = TaskbarLiveScanSignature.Create(
            CreateDiscovery(ignoredHostMatch: true),
            TaskbarPlacementResult.VerifiedNoFit(PlacementReason.InsufficientWidth));
        TaskbarLiveScanSignature raced = noFit.WithInvalidatedDuringScan(value: true);

        Assert.NotEqual(incomplete, noFit);
        Assert.NotEqual(noFit, raced);
        Assert.False(incomplete.DiscoveryComplete);
        Assert.Equal(PlacementDecision.TransientUnknown, incomplete.Decision);
        Assert.Equal(PlacementReason.IncompleteObservation, incomplete.Reason);
        Assert.Equal(nameof(TaskbarDiscoveryFaultCode.PrimaryTaskbarMissing), incomplete.FaultCodes);
        Assert.Equal(PlacementDecision.VerifiedNoFit, noFit.Decision);
        Assert.True(noFit.DiscoveryComplete);
        Assert.Equal(PlacementReason.InsufficientWidth, noFit.Reason);
        Assert.Equal("None", noFit.FaultCodes);
        Assert.True(noFit.IgnoredHostMatch);
        Assert.False(noFit.InvalidatedDuringScan);
        Assert.True(raced.InvalidatedDuringScan);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnexpectedFailure_PreservesKnownIgnoredHostMatch(bool ignoredHostMatch)
    {
        TaskbarDiscoveryResult result =
            TaskbarDiscoveryService.CreateUnexpectedFailure(ignoredHostMatch);

        Assert.False(result.IsComplete);
        Assert.Equal(ignoredHostMatch, result.IgnoredHostMatch);
        TaskbarDiscoveryFault fault = Assert.Single(result.Faults);
        Assert.Equal(TaskbarDiscoveryFaultCode.UnexpectedDiscoveryFailure, fault.Code);
    }

    [Fact]
    public void NativeChildPolicy_RecordsOnlyAnExactIgnoredHostMatch()
    {
        bool ignoredHostMatch = false;

        Assert.False(TaskbarNativeChildPolicy.ShouldIgnoreAndRecordMatch(
            candidate: 40,
            ignoredWindowHandle: 41,
            ref ignoredHostMatch));
        Assert.False(ignoredHostMatch);
        Assert.True(TaskbarNativeChildPolicy.ShouldIgnoreAndRecordMatch(
            candidate: 41,
            ignoredWindowHandle: 41,
            ref ignoredHostMatch));
        Assert.True(ignoredHostMatch);
        Assert.False(TaskbarNativeChildPolicy.ShouldIgnoreAndRecordMatch(
            candidate: 42,
            ignoredWindowHandle: 41,
            ref ignoredHostMatch));
        Assert.True(ignoredHostMatch);
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
