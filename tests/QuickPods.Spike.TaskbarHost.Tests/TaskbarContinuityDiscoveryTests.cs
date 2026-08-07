using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class TaskbarContinuityDiscoveryTests
{
    private static readonly PixelRect TaskbarBounds = new(0, 1040, 1920, 1080);
    private static readonly PixelRect MonitorBounds = new(0, 0, 1920, 1080);
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void Anchor_requires_and_captures_a_complete_primary_discovery()
    {
        TaskbarDiscoveryResult discovery = CreateCompleteDiscovery();

        var anchor =
            TaskbarContinuityAnchor.FromCompleteDiscovery(discovery);

        Assert.Equal(new nint(42), anchor.TaskbarHandle);
        Assert.Equal((uint)84, anchor.ExplorerProcessId);
        Assert.Equal(TaskbarBounds, anchor.TaskbarBounds);
        Assert.Equal((uint)144, anchor.Dpi);
        Assert.Equal(MonitorBounds, anchor.MonitorBounds);
        Assert.Equal(WorkArea, anchor.WorkArea);
        Assert.Equal("StartButton", anchor.RetainedStartButton.AutomationId);
        Assert.Equal(new PixelRect(900, 1040, 940, 1080), anchor.RetainedStartButton.Bounds);
        Assert.Equal(nameof(TaskbarContinuityAnchor), anchor.ToString());
        Assert.DoesNotContain("42", anchor.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("84", anchor.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Anchor_advances_start_only_from_complete_same_generation_automation()
    {
        var anchor =
            TaskbarContinuityAnchor.FromCompleteDiscovery(CreateCompleteDiscovery());
        var freshStart = new AutomationButtonSnapshot(
            "StartButton",
            new PixelRect(910, 1040, 950, 1080),
            false);
        var freshDiscovery = new TaskbarDiscoveryResult(
            CreateSnapshot(automationButtons: [freshStart]),
            faults: []);

        bool updated = anchor.TryWithFreshCompleteAutomation(
            freshDiscovery,
            out TaskbarContinuityAnchor next);

        Assert.True(updated);
        Assert.NotSame(anchor, next);
        Assert.Equal(freshStart, next.RetainedStartButton);
        Assert.Equal(new PixelRect(900, 1040, 940, 1080), anchor.RetainedStartButton.Bounds);
    }

    [Fact]
    public void Anchor_never_advances_from_partial_changed_or_landmark_missing_observations()
    {
        var anchor =
            TaskbarContinuityAnchor.FromCompleteDiscovery(CreateCompleteDiscovery());
        TaskbarDiscoveryResult[] rejected =
        [
            new TaskbarDiscoveryResult(
                CreateSnapshot(),
                [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.StartButtonMissing)]),
            new TaskbarDiscoveryResult(
                CreateSnapshot(explorerProcessId: 85),
                faults: []),
            new TaskbarDiscoveryResult(
                CreateSnapshot(automationButtons: []),
                faults: []),
        ];

        foreach (TaskbarDiscoveryResult discovery in rejected)
        {
            Assert.False(anchor.TryWithFreshCompleteAutomation(discovery, out TaskbarContinuityAnchor next));
            Assert.Same(anchor, next);
        }
    }

    [Fact]
    public void Top_level_selection_uses_the_unique_expected_handle_when_enumerated()
    {
        TaskbarContinuityTopLevelDecision decision =
            Win32TaskbarDiscovery.SelectContinuityRoute(
                new nint(42),
                [new nint(42)],
                []);

        Assert.Equal(TaskbarContinuityRoute.EnumeratedExpected, decision.Route);
        Assert.Equal(TaskbarContinuityFailureReason.None, decision.FailureReason);
        Assert.True(decision.Evidence.ExpectedHandleSelected);
        Assert.True(decision.Evidence.ExpectedHandleWasEnumerated);
        Assert.True(decision.Evidence.NoCompetingPrimaryTaskbar);
    }

    [Fact]
    public void Top_level_selection_uses_direct_expected_only_when_none_were_enumerated()
    {
        TaskbarContinuityTopLevelDecision decision =
            Win32TaskbarDiscovery.SelectContinuityRoute(new nint(42), [], []);

        Assert.Equal(TaskbarContinuityRoute.DirectExpected, decision.Route);
        Assert.Equal(TaskbarContinuityFailureReason.None, decision.FailureReason);
        Assert.True(decision.Evidence.ExpectedHandleSelected);
        Assert.False(decision.Evidence.ExpectedHandleWasEnumerated);
        Assert.True(decision.Evidence.NoCompetingPrimaryTaskbar);
    }

    [Theory]
    [InlineData(true)]
    public void Top_level_selection_rejects_a_different_or_duplicate_taskbar(bool duplicate)
    {
        nint[] handles = duplicate
            ? [new nint(42), new nint(43)]
            : [new nint(43)];

        TaskbarContinuityTopLevelDecision decision =
            Win32TaskbarDiscovery.SelectContinuityRoute(new nint(42), handles, []);

        Assert.Equal(TaskbarContinuityRoute.None, decision.Route);
        Assert.Equal(
            duplicate
                ? TaskbarContinuityFailureReason.PrimaryTaskbarDuplicate
                : TaskbarContinuityFailureReason.ExpectedTaskbarChanged,
            decision.FailureReason);
        Assert.False(decision.Evidence.NoCompetingPrimaryTaskbar);
    }

    [Theory]
    [InlineData(
        (int)TaskbarDiscoveryFaultCode.TopLevelEnumerationFailed,
        (int)TaskbarContinuityFailureReason.TopLevelEnumerationFailed)]
    public void Top_level_selection_never_uses_direct_expected_with_other_faults(
        int faultCodeValue,
        int expectedReasonValue)
    {
        TaskbarContinuityTopLevelDecision decision =
            Win32TaskbarDiscovery.SelectContinuityRoute(
                new nint(42),
                [],
                [new TaskbarDiscoveryFault((TaskbarDiscoveryFaultCode)faultCodeValue)]);

        Assert.Equal(TaskbarContinuityRoute.None, decision.Route);
        Assert.Equal(
            (TaskbarContinuityFailureReason)expectedReasonValue,
            decision.FailureReason);
        Assert.False(decision.Evidence.ExpectedHandleSelected);
    }

    [Fact]
    public void Evidence_requires_every_identity_geometry_cloak_and_scan_proof()
    {
        TaskbarContinuityEvidence verified = CreateVerifiedEvidence();
        var cases = new (TaskbarContinuityFailureReason Reason, TaskbarContinuityEvidence Evidence)[]
        {
            (TaskbarContinuityFailureReason.TopLevelEnumerationFailed,
                verified with { TopLevelEnumerationSucceeded = false }),
            (TaskbarContinuityFailureReason.TopLevelClassReadFailed,
                verified with { TopLevelClassReadsSucceeded = false }),
            (TaskbarContinuityFailureReason.ExpectedTaskbarChanged,
                verified with { NoCompetingPrimaryTaskbar = false }),
            (TaskbarContinuityFailureReason.ExpectedTaskbarChanged,
                verified with { ExpectedHandleSelected = false }),
            (TaskbarContinuityFailureReason.ExistingHostUnavailable,
                verified with { ExistingHostSupplied = false }),
            (TaskbarContinuityFailureReason.ExpectedWindowUnavailable,
                verified with { ExpectedWindowLive = false }),
            (TaskbarContinuityFailureReason.ClassMismatch,
                verified with { ClassMatched = false }),
            (TaskbarContinuityFailureReason.RootMismatch,
                verified with { RootMatched = false }),
            (TaskbarContinuityFailureReason.NotVisible,
                verified with { ParentVisible = false }),
            (TaskbarContinuityFailureReason.CloakStateUnavailable,
                verified with { CloakStateAvailable = false }),
            (TaskbarContinuityFailureReason.Cloaked,
                verified with { Uncloaked = false }),
            (TaskbarContinuityFailureReason.ProcessMismatch,
                verified with { ProcessMatched = false }),
            (TaskbarContinuityFailureReason.BoundsMismatch,
                verified with { BoundsMatched = false }),
            (TaskbarContinuityFailureReason.DpiMismatch,
                verified with { DpiMatched = false }),
            (TaskbarContinuityFailureReason.MonitorUnavailable,
                verified with { MonitorAvailable = false }),
            (TaskbarContinuityFailureReason.NotPrimaryMonitor,
                verified with { PrimaryMonitorMatched = false }),
            (TaskbarContinuityFailureReason.MonitorBoundsMismatch,
                verified with { MonitorBoundsMatched = false }),
            (TaskbarContinuityFailureReason.WorkAreaMismatch,
                verified with { WorkAreaMatched = false }),
            (TaskbarContinuityFailureReason.NativeChildrenIncomplete,
                verified with { NativeChildrenComplete = false }),
            (TaskbarContinuityFailureReason.ExistingHostNotAttached,
                verified with { IgnoredHostMatched = false }),
            (TaskbarContinuityFailureReason.AutomationIncomplete,
                verified with
                {
                    AutomationComplete = false,
                    AutomationOrigin = TaskbarAutomationContinuityOrigin.None,
                }),
            (TaskbarContinuityFailureReason.AutomationProvenanceInvalid,
                verified with { AutomationOrigin = TaskbarAutomationContinuityOrigin.None }),
        };

        Assert.True(verified.IsVerified);
        foreach ((TaskbarContinuityFailureReason reason, TaskbarContinuityEvidence evidence) in cases)
        {
            Assert.False(evidence.IsVerified);
            Assert.Equal(
                reason,
                TaskbarContinuityEvidencePolicy.GetFailureReason(
                    evidence,
                    requireNativeChildren: true,
                    requireAutomation: true));
        }
    }

    [Fact]
    public void Direct_verification_does_not_require_the_expected_handle_to_be_enumerated()
    {
        TaskbarContinuityEvidence evidence = CreateVerifiedEvidence() with
        {
            ExpectedHandleWasEnumerated = false,
        };

        Assert.True(evidence.IsVerified);
    }

    [Theory]
    [InlineData(
        (int)TaskbarDiscoveryFaultCode.NotificationAreaMissing,
        (int)TaskbarContinuityFailureReason.NotificationAreaMissing)]
    public void Native_child_faults_are_reported_without_raw_window_data(
        int faultCodeValue,
        int reasonValue)
    {
        TaskbarContinuityFailureReason reason =
            Win32TaskbarDiscovery.GetNativeChildrenFailureReason(
                [new TaskbarDiscoveryFault((TaskbarDiscoveryFaultCode)faultCodeValue)]);

        Assert.Equal((TaskbarContinuityFailureReason)reasonValue, reason);
    }

    [Theory]
    [InlineData(
        (int)TaskbarDiscoveryFaultCode.StartButtonMissing,
        (int)TaskbarContinuityFailureReason.StartButtonMissing)]
    public void Automation_faults_are_reported_without_raw_window_data(
        int faultCodeValue,
        int reasonValue)
    {
        TaskbarContinuityFailureReason reason =
            TaskbarDiscoveryService.GetAutomationFailureReason(
                [new TaskbarDiscoveryFault((TaskbarDiscoveryFaultCode)faultCodeValue)]);

        Assert.Equal((TaskbarContinuityFailureReason)reasonValue, reason);
    }

    [Fact]
    public void Unknown_or_empty_automation_faults_remain_fail_closed()
    {
        Assert.Equal(
            TaskbarContinuityFailureReason.AutomationIncomplete,
            TaskbarDiscoveryService.GetAutomationFailureReason([]));
        Assert.Equal(
            TaskbarContinuityFailureReason.AutomationIncomplete,
            TaskbarDiscoveryService.GetAutomationFailureReason(
                [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.PrimaryTaskbarMissing)]));
    }

    [Fact]
    public void Direct_start_missing_can_retain_only_the_anchored_start_landmark()
    {
        var freshButton = new AutomationButtonSnapshot(
            "PinnedApp",
            new PixelRect(1000, 1040, 1040, 1080),
            false);
        TaskbarContinuityAnchor anchor =
            TaskbarContinuityAnchor.FromCompleteDiscovery(CreateCompleteDiscovery());
        var automation = new AutomationTaskbarProbe(
            [freshButton],
            [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.StartButtonMissing)]);

        bool retained = TaskbarDiscoveryService.TryRetainStartButtonContinuity(
            TaskbarContinuityRoute.DirectExpected,
            TaskbarBounds,
            automation,
            anchor,
            out RetainedStartButtonContinuityProof? proof);

        Assert.True(retained);
        Assert.NotNull(proof);
        Assert.Equal([freshButton, anchor.RetainedStartButton], proof.Buttons);
        Assert.Equal(nameof(RetainedStartButtonContinuityProof), proof.ToString());
        Assert.DoesNotContain("42", proof.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Start_retention_rejects_non_direct_extra_or_contradictory_evidence()
    {
        TaskbarContinuityAnchor anchor =
            TaskbarContinuityAnchor.FromCompleteDiscovery(CreateCompleteDiscovery());
        var exactMissing = new AutomationTaskbarProbe(
            [],
            [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.StartButtonMissing)]);
        var extraFault = new AutomationTaskbarProbe(
            [],
            [
                new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.StartButtonMissing),
                new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.AutomationPropertyUnavailable),
            ]);
        var contradictoryStart = new AutomationTaskbarProbe(
            [anchor.RetainedStartButton],
            [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.StartButtonMissing)]);
        var outsideFreshButton = new AutomationTaskbarProbe(
            [new AutomationButtonSnapshot(
                "PinnedApp",
                new PixelRect(100, 100, 140, 140),
                false)],
            [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.StartButtonMissing)]);

        Assert.False(TryRetain(TaskbarContinuityRoute.EnumeratedExpected, exactMissing, anchor));
        Assert.False(TryRetain(TaskbarContinuityRoute.DirectExpected, extraFault, anchor));
        Assert.False(TryRetain(TaskbarContinuityRoute.DirectExpected, contradictoryStart, anchor));
        Assert.False(TryRetain(TaskbarContinuityRoute.DirectExpected, outsideFreshButton, anchor));

        static bool TryRetain(
            TaskbarContinuityRoute route,
            AutomationTaskbarProbe automation,
            TaskbarContinuityAnchor continuityAnchor) =>
            TaskbarDiscoveryService.TryRetainStartButtonContinuity(
                route,
                TaskbarBounds,
                automation,
                continuityAnchor,
                out _);
    }

    [Fact]
    public void Retained_start_button_evidence_requires_matching_anchor_proof()
    {
        var freshButton = new AutomationButtonSnapshot(
            "PinnedApp",
            new PixelRect(1000, 1040, 1040, 1080),
            false);
        TaskbarContinuityAnchor anchor =
            TaskbarContinuityAnchor.FromCompleteDiscovery(CreateCompleteDiscovery());
        var automation = new AutomationTaskbarProbe(
            [freshButton],
            [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.StartButtonMissing)]);
        Assert.True(TaskbarDiscoveryService.TryRetainStartButtonContinuity(
            TaskbarContinuityRoute.DirectExpected,
            TaskbarBounds,
            automation,
            anchor,
            out RetainedStartButtonContinuityProof? proof));
        Assert.NotNull(proof);
        TaskbarContinuityEvidence evidence = CreateVerifiedEvidence() with
        {
            AutomationComplete = false,
            AutomationOrigin =
                TaskbarAutomationContinuityOrigin.RetainedStartFromCompleteAnchor,
        };
        var discovery = new TaskbarDiscoveryResult(
            CreateSnapshot(automationButtons: proof.Buttons),
            faults: []);

        Assert.True(evidence.IsVerified);
        _ = Assert.Throws<ArgumentException>(() =>
            TaskbarContinuityDiscoveryResult.Verified(
                discovery,
                evidence,
                TaskbarContinuityRoute.DirectExpected));
        var changedGeneration = new TaskbarDiscoveryResult(
            CreateSnapshot(
                explorerProcessId: 85,
                automationButtons: proof.Buttons),
            faults: []);
        _ = Assert.Throws<ArgumentException>(() =>
            TaskbarContinuityDiscoveryResult.Verified(
                changedGeneration,
                evidence,
                TaskbarContinuityRoute.DirectExpected,
                proof));
        var direct = TaskbarContinuityDiscoveryResult.Verified(
            discovery,
            evidence,
            TaskbarContinuityRoute.DirectExpected,
            proof);

        Assert.True(direct.IsVerified);
        _ = Assert.Throws<ArgumentException>(() =>
            TaskbarContinuityDiscoveryResult.Verified(
                discovery,
                evidence,
                TaskbarContinuityRoute.EnumeratedExpected,
                proof));
        _ = Assert.Throws<ArgumentException>(() =>
            TaskbarContinuityDiscoveryResult.Verified(
                discovery,
                evidence with { AutomationComplete = true },
                TaskbarContinuityRoute.DirectExpected,
                proof));
    }

    [Fact]
    public void Notification_area_missing_is_retained_only_for_direct_continuity()
    {
        TaskbarDiscoveryFault[] missingNotificationArea =
        [new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.NotificationAreaMissing)];

        Assert.True(
            Win32TaskbarDiscovery.CanRetainNotificationAreaContinuity(
                TaskbarContinuityRoute.DirectExpected,
                missingNotificationArea));
        Assert.False(
            Win32TaskbarDiscovery.CanRetainNotificationAreaContinuity(
                TaskbarContinuityRoute.EnumeratedExpected,
                missingNotificationArea));
        Assert.False(
            Win32TaskbarDiscovery.CanRetainNotificationAreaContinuity(
                TaskbarContinuityRoute.DirectExpected,
                [
                    .. missingNotificationArea,
                    new TaskbarDiscoveryFault(TaskbarDiscoveryFaultCode.ChildClassReadFailed),
                ]));
    }

    [Fact]
    public void Retained_notification_area_evidence_is_valid_only_on_direct_results()
    {
        TaskbarContinuityEvidence evidence = CreateVerifiedEvidence() with
        {
            NativeChildrenComplete = false,
            RetainedNotificationAreaContinuity = true,
        };
        TaskbarDiscoveryResult discovery = CreateCompleteDiscovery();

        Assert.True(evidence.IsVerified);
        var direct = TaskbarContinuityDiscoveryResult.Verified(
            discovery,
            evidence,
            TaskbarContinuityRoute.DirectExpected);

        Assert.True(direct.IsVerified);
        _ = Assert.Throws<ArgumentException>(() =>
            TaskbarContinuityDiscoveryResult.Verified(
                discovery,
                evidence,
                TaskbarContinuityRoute.EnumeratedExpected));
    }

    [Fact]
    public void Direct_attachment_evidence_can_replace_transient_child_enumeration_match()
    {
        TaskbarContinuityEvidence evidence = CreateVerifiedEvidence() with
        {
            IgnoredHostMatched = false,
            DirectHostAttachmentVerified = true,
        };
        TaskbarDiscoveryResult discovery = CreateCompleteDiscovery();

        Assert.True(evidence.IsVerified);
        var direct = TaskbarContinuityDiscoveryResult.Verified(
            discovery,
            evidence,
            TaskbarContinuityRoute.DirectExpected);

        Assert.True(direct.IsVerified);
        _ = Assert.Throws<ArgumentException>(() =>
            TaskbarContinuityDiscoveryResult.Verified(
                discovery,
                evidence,
                TaskbarContinuityRoute.EnumeratedExpected));
    }

    [Fact]
    public void Continuity_result_text_contains_only_sanitized_route_reason_and_boolean_evidence()
    {
        TaskbarDiscoveryResult discovery = CreateCompleteDiscovery();
        var result =
            TaskbarContinuityDiscoveryResult.Verified(
                discovery,
                CreateVerifiedEvidence(),
                TaskbarContinuityRoute.DirectExpected);

        string text = result.ToString();

        Assert.True(result.IsVerified);
        Assert.Same(discovery, result.Discovery);
        Assert.Contains(nameof(TaskbarContinuityRoute.DirectExpected), text, StringComparison.Ordinal);
        Assert.Contains("Evidence", text, StringComparison.Ordinal);
        Assert.DoesNotContain("42", text, StringComparison.Ordinal);
        Assert.DoesNotContain("84", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TaskbarBounds.ToString(), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Timed_out_result_is_fail_closed_and_sanitized()
    {
        var result =
            TaskbarContinuityDiscoveryResult.Failed(
                default,
                TaskbarContinuityRoute.None,
                TaskbarContinuityFailureReason.TimedOut);

        string text = result.ToString();

        Assert.False(result.IsVerified);
        Assert.Null(result.Discovery);
        Assert.Equal(TaskbarContinuityFailureReason.TimedOut, result.FailureReason);
        Assert.Contains(nameof(TaskbarContinuityFailureReason.TimedOut), text, StringComparison.Ordinal);
        Assert.DoesNotContain("42", text, StringComparison.Ordinal);
        Assert.DoesNotContain("84", text, StringComparison.Ordinal);
    }

    private static TaskbarContinuityEvidence CreateVerifiedEvidence() =>
        new()
        {
            TopLevelEnumerationSucceeded = true,
            TopLevelClassReadsSucceeded = true,
            NoCompetingPrimaryTaskbar = true,
            ExpectedHandleSelected = true,
            ExpectedHandleWasEnumerated = true,
            ExistingHostSupplied = true,
            ExpectedWindowLive = true,
            ClassMatched = true,
            RootMatched = true,
            ParentVisible = true,
            CloakStateAvailable = true,
            Uncloaked = true,
            ProcessMatched = true,
            BoundsMatched = true,
            DpiMatched = true,
            MonitorAvailable = true,
            PrimaryMonitorMatched = true,
            MonitorBoundsMatched = true,
            WorkAreaMatched = true,
            NativeChildrenComplete = true,
            AutomationComplete = true,
            AutomationOrigin = TaskbarAutomationContinuityOrigin.FreshComplete,
            IgnoredHostMatched = true,
        };

    private static TaskbarDiscoveryResult CreateCompleteDiscovery() =>
        new(CreateSnapshot(), faults: []);

    private static TaskbarSnapshot CreateSnapshot(
        nint? taskbarHandle = null,
        uint explorerProcessId = 84,
        PixelRect? taskbarBounds = null,
        uint dpi = 144,
        PixelRect? monitorBounds = null,
        PixelRect? workArea = null,
        bool isPrimary = true,
        IReadOnlyList<AutomationButtonSnapshot>? automationButtons = null) =>
        new(
            taskbarHandle ?? new nint(42),
            explorerProcessId,
            taskbarBounds ?? TaskbarBounds,
            dpi,
            new TaskbarMonitorSnapshot(
                monitorBounds ?? MonitorBounds,
                workArea ?? WorkArea,
                isPrimary),
            [new CriticalTaskbarChildSnapshot(
                CriticalTaskbarChildKind.NotificationArea,
                new PixelRect(1700, 1040, 1920, 1080))],
            automationButtons ??
            [
                new AutomationButtonSnapshot(
                    "StartButton",
                    new PixelRect(900, 1040, 940, 1080),
                    false),
            ]);
}
