using QuickPods.Spike.TaskbarHost.Discovery;
using QuickPods.Spike.TaskbarHost.Runtime;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class TaskbarAutomationWatcherTests
{
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(101, 101, true)]
    public void NativeChildPolicy_IgnoresOnlyExactNonZeroHostedWindow(
        long candidate,
        long ignored,
        bool expected)
    {
        Assert.Equal(
            expected,
            TaskbarNativeChildPolicy.ShouldIgnore(new nint(candidate), new nint(ignored)));
    }

    [Theory]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, false)]
    public void EventPolicy_FiltersKnownNonButtonPropertySendersOnly(
        bool placementButtonsOnly,
        bool controlTypeKnown,
        bool isPlacementButton,
        bool expected)
    {
        Assert.Equal(
            expected,
            TaskbarAutomationEventPolicy.ShouldSignal(
                placementButtonsOnly,
                controlTypeKnown,
                isPlacementButton));
    }

    [Theory]
    [InlineData(
        (int)TaskbarAutomationEventKind.StructureChanged,
        (int)TaskbarAutomationEventSourceClass.External,
        (int)TaskbarContinuityRoute.DirectExpected,
        (int)TaskbarContinuityRoute.None,
        true)]
    [InlineData(
        (int)TaskbarAutomationEventKind.StructureChanged,
        (int)TaskbarAutomationEventSourceClass.External,
        (int)TaskbarContinuityRoute.EnumeratedExpected,
        (int)TaskbarContinuityRoute.DirectExpected,
        true)]
    [InlineData(
        (int)TaskbarAutomationEventKind.StructureChanged,
        (int)TaskbarAutomationEventSourceClass.External,
        (int)TaskbarContinuityRoute.EnumeratedExpected,
        (int)TaskbarContinuityRoute.EnumeratedExpected,
        false)]
    [InlineData(
        (int)TaskbarAutomationEventKind.BoundingRectangleChanged,
        (int)TaskbarAutomationEventSourceClass.External,
        (int)TaskbarContinuityRoute.DirectExpected,
        (int)TaskbarContinuityRoute.None,
        false)]
    public void NativeContinuityInvalidationPolicy_AllowsOnlyDirectTransitionStructureChanges(
        int kindValue,
        int sourceClassValue,
        int preflightRouteValue,
        int lastVerifiedRouteValue,
        bool expected)
    {
        var invalidation = new TaskbarAutomationInvalidation(
            (TaskbarAutomationEventKind)kindValue,
            (TaskbarAutomationEventSourceClass)sourceClassValue);

        Assert.Equal(
            expected,
            TaskbarNativeContinuityInvalidationPolicy.CanProbeWhileVisible(
                invalidation,
                (TaskbarContinuityRoute)preflightRouteValue,
                (TaskbarContinuityRoute)lastVerifiedRouteValue));
    }

    [Fact]
    public void NativeContinuityInvalidationPolicy_RejectsAStickyHideFirstBatch()
    {
        var invalidation = new TaskbarAutomationInvalidation(
            TaskbarAutomationEventKind.StructureChanged,
            TaskbarAutomationEventSourceClass.External,
            RequiresHideFirst: true);

        Assert.False(
            TaskbarNativeContinuityInvalidationPolicy.CanProbeWhileVisible(
                invalidation,
                TaskbarContinuityRoute.DirectExpected,
                TaskbarContinuityRoute.None));
    }

    [Fact]
    public void Signal_CoalescingKeepsHideFirstStickyUntilConsumed()
    {
        var signal = new TaskbarAutomationInvalidationSignal();
        long observed = signal.Generation;

        signal.Signal(new(
            NativeWindowHandle: 100,
            ProcessId: 200,
            TaskbarAutomationEventKind.BoundingRectangleChanged));
        signal.Signal(new(
            NativeWindowHandle: 100,
            ProcessId: 200,
            TaskbarAutomationEventKind.StructureChanged));

        Assert.True(signal.TryConsume(ref observed, out TaskbarAutomationInvalidation batch));
        Assert.Equal(TaskbarAutomationEventKind.StructureChanged, batch.Kind);
        Assert.Equal(TaskbarAutomationEventSourceClass.External, batch.SourceClass);
        Assert.True(batch.RequiresHideFirst);

        signal.Signal(new(
            NativeWindowHandle: 100,
            ProcessId: 200,
            TaskbarAutomationEventKind.StructureChanged));

        Assert.True(signal.TryConsume(ref observed, out TaskbarAutomationInvalidation next));
        Assert.False(next.RequiresHideFirst);

        signal.Signal(new(
            NativeWindowHandle: 0,
            ProcessId: 0,
            TaskbarAutomationEventKind.StructureChanged));
        signal.Signal(new(
            NativeWindowHandle: 100,
            ProcessId: 200,
            TaskbarAutomationEventKind.StructureChanged));

        Assert.True(signal.TryConsume(ref observed, out TaskbarAutomationInvalidation mixed));
        Assert.Equal(TaskbarAutomationEventSourceClass.External, mixed.SourceClass);
        Assert.True(mixed.RequiresHideFirst);
    }

    [Fact]
    public void Signal_FiltersOwnedVisibilityButNotOwnedBoundsOrUnknownSender()
    {
        var signal = new TaskbarAutomationInvalidationSignal();
        long observed = signal.Generation;
        nint hostedWindow = new(0x12345678);
        signal.SetIgnoredWindowHandle(hostedWindow);

        signal.Signal(new(
            unchecked((int)hostedWindow.ToInt64()),
            Environment.ProcessId,
            TaskbarAutomationEventKind.IsOffscreenChanged));

        Assert.False(signal.TryConsume(ref observed));

        signal.Signal(new(
            unchecked((int)hostedWindow.ToInt64()),
            Environment.ProcessId,
            TaskbarAutomationEventKind.BoundingRectangleChanged));

        Assert.True(signal.TryConsume(ref observed, out TaskbarAutomationInvalidation ownedBounds));
        Assert.Equal(
            new(
                TaskbarAutomationEventKind.BoundingRectangleChanged,
                TaskbarAutomationEventSourceClass.Owned,
                RequiresHideFirst: true),
            ownedBounds);

        signal.Signal(new(
            NativeWindowHandle: 0,
            ProcessId: 0,
            TaskbarAutomationEventKind.StructureChanged));

        Assert.True(signal.TryConsume(ref observed, out TaskbarAutomationInvalidation unknownStructure));
        Assert.Equal(
            new(
                TaskbarAutomationEventKind.StructureChanged,
                TaskbarAutomationEventSourceClass.Unknown,
                RequiresHideFirst: true),
            unknownStructure);
        Assert.False(signal.TryConsume(ref observed));
    }

    [Fact]
    public void Signal_CoalescingReturnsLatestAcceptedClassification()
    {
        var signal = new TaskbarAutomationInvalidationSignal();
        long observed = signal.Generation;

        signal.Signal(new(
            NativeWindowHandle: 100,
            ProcessId: 200,
            TaskbarAutomationEventKind.StructureChanged));
        signal.Signal(new(
            NativeWindowHandle: 0,
            ProcessId: 0,
            TaskbarAutomationEventKind.IsOffscreenChanged));

        Assert.True(signal.TryConsume(ref observed, out TaskbarAutomationInvalidation accepted));
        Assert.Equal(
            new(
                TaskbarAutomationEventKind.IsOffscreenChanged,
                TaskbarAutomationEventSourceClass.Unknown,
                RequiresHideFirst: true),
            accepted);
        Assert.Equal(2, observed);
    }

    [Theory]
    [InlineData(101, 101, 4, 4, true)]
    [InlineData(101, 202, 4, 4, false)]
    [InlineData(101, 101, 4, 5, false)]
    public void ArmingFence_RequiresSameNonZeroRootAndUnchangedGeneration(
        long watchedHandle,
        long discoveredHandle,
        long generationBefore,
        long generationAfter,
        bool expected)
    {
        Assert.Equal(
            expected,
            TaskbarAutomationArmingFence.IsStable(
                new nint(watchedHandle),
                new nint(discoveredHandle),
                generationBefore,
                generationAfter));
    }

    [Fact]
    public void Signal_ConcurrentCallbacksAdvanceOneAtomicGenerationEach()
    {
        var signal = new TaskbarAutomationInvalidationSignal();

        Parallel.For(0, 1_000, index => signal.Signal(new(index + 1, ProcessId: 1)));

        Assert.Equal(1_000, signal.Generation);
    }

    [Fact]
    public void Signal_ConcurrentCallbacksPublishUntornGenerationAndClassification()
    {
        var signal = new TaskbarAutomationInvalidationSignal();
        long observed = signal.Generation;
        int externalProcessId = Environment.ProcessId == int.MaxValue
            ? Environment.ProcessId - 1
            : Environment.ProcessId + 1;
        var owned = new TaskbarAutomationEventSource(
            NativeWindowHandle: 0,
            ProcessId: Environment.ProcessId,
            TaskbarAutomationEventKind.BoundingRectangleChanged);
        var external = new TaskbarAutomationEventSource(
            NativeWindowHandle: 100,
            ProcessId: externalProcessId,
            TaskbarAutomationEventKind.IsOffscreenChanged);

        Parallel.For(
            0,
            1_000,
            index => signal.Signal(index % 2 == 0 ? owned : external));

        Assert.True(signal.TryConsume(ref observed, out TaskbarAutomationInvalidation accepted));
        Assert.Equal(1_000, observed);
        TaskbarAutomationInvalidation[] validClassifications =
        [
            new(
                TaskbarAutomationEventKind.BoundingRectangleChanged,
                TaskbarAutomationEventSourceClass.Owned,
                RequiresHideFirst: true),
            new(
                TaskbarAutomationEventKind.IsOffscreenChanged,
                TaskbarAutomationEventSourceClass.External,
                RequiresHideFirst: true),
        ];
        Assert.Contains(accepted, validClassifications);
    }

    [Fact]
    public void Watcher_SubscribeReplaceAndDispose_RunOnOneDedicatedMtaThread()
    {
        var signal = new TaskbarAutomationInvalidationSignal();
        var subscription = new RecordingSubscription();
        using var watcher = TaskbarAutomationWatcher.Start(
            new nint(101),
            signal,
            () => subscription);

        Assert.False(watcher.ReplaceTaskbar(new nint(101)));
        Assert.True(watcher.ReplaceTaskbar(new nint(202)));
        Assert.Equal(new nint(202), watcher.TaskbarHandle);
        watcher.Dispose();

        Assert.Equal(
            ["subscribe:101", "unsubscribe", "subscribe:202", "unsubscribe"],
            subscription.Operations);
        Assert.Single(subscription.ThreadIds.Distinct());
        Assert.All(
            subscription.ApartmentStates,
            state => Assert.Equal(ApartmentState.MTA, state));
        Assert.NotEqual(Environment.CurrentManagedThreadId, subscription.ThreadIds[0]);
    }

    [Fact]
    public void Watcher_DisposeFailureStillCompletesShutdown()
    {
        var signal = new TaskbarAutomationInvalidationSignal();
        var subscription = new RecordingSubscription
        {
            FailingUnsubscribeCall = 1,
        };
        var watcher = TaskbarAutomationWatcher.Start(
            new nint(101),
            signal,
            () => subscription);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            watcher.Dispose);

        Assert.Equal("Synthetic unsubscription failure.", exception.Message);
        Assert.Equal(["subscribe:101", "unsubscribe"], subscription.Operations);
        watcher.Dispose();
    }

    [Fact]
    public void Watcher_SubscriptionFailureFailsClosed()
    {
        var signal = new TaskbarAutomationInvalidationSignal();
        var subscription = new RecordingSubscription
        {
            FailingSubscribeCall = 1,
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TaskbarAutomationWatcher.Start(
                new nint(101),
                signal,
                () => subscription));

        Assert.Equal("Synthetic subscription failure.", exception.Message);
        Assert.Single(subscription.ThreadIds.Distinct());
    }

    [Fact]
    public void Watcher_RootReplacementFailureLeavesNewRootUnpublished()
    {
        var signal = new TaskbarAutomationInvalidationSignal();
        var subscription = new RecordingSubscription
        {
            FailingSubscribeCall = 2,
        };
        using var watcher = TaskbarAutomationWatcher.Start(
            new nint(101),
            signal,
            () => subscription);

        _ = Assert.Throws<InvalidOperationException>(
            () => watcher.ReplaceTaskbar(new nint(202)));

        Assert.Equal(new nint(101), watcher.TaskbarHandle);
        Assert.Equal(
            ["subscribe:101", "unsubscribe", "subscribe:202"],
            subscription.Operations);
    }

    [Fact]
    public void Watcher_RootReplacementRejectsInFlightCallbacksFromOldEpoch()
    {
        var signal = new TaskbarAutomationInvalidationSignal();
        long observed = signal.Generation;
        var subscription = new RecordingSubscription();
        using var watcher = TaskbarAutomationWatcher.Start(
            new nint(101),
            signal,
            () => subscription);

        Assert.True(watcher.ReplaceTaskbar(new nint(202)));
        subscription.SignalFromSubscription(0, new(NativeWindowHandle: 303, ProcessId: 1));

        Assert.False(signal.TryConsume(ref observed));

        subscription.SignalFromSubscription(1, new(NativeWindowHandle: 303, ProcessId: 1));

        Assert.True(signal.TryConsume(ref observed));
    }

    [Fact]
    public void LayoutRootPolicy_SelectsImmediateParentOfUniqueAnchor()
    {
        var root = new AutomationNode("root");
        var layout = new AutomationNode("layout", root);
        var anchor = new AutomationNode("anchor", layout);

        AutomationNode selected = TaskbarAutomationLayoutRootPolicy.SelectUniqueImmediateParent(
            [anchor],
            root,
            static node => node.Parent,
            static (left, right) => ReferenceEquals(left, right));

        Assert.Same(layout, selected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void LayoutRootPolicy_RejectsMissingOrAmbiguousAnchor(int anchorCount)
    {
        var root = new AutomationNode("root");
        AutomationNode[] anchors =
            [.. Enumerable.Range(0, anchorCount)
                .Select(index => new AutomationNode($"anchor-{index}", root))];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TaskbarAutomationLayoutRootPolicy.SelectUniqueImmediateParent(
                anchors,
                root,
                static node => node.Parent,
                static (left, right) => ReferenceEquals(left, right)));

        Assert.Equal("The taskbar layout anchor is unavailable or ambiguous.", exception.Message);
    }

    [Fact]
    public void LayoutRootPolicy_RejectsTaskbarRootAsImmediateParent()
    {
        var root = new AutomationNode("root");
        var anchor = new AutomationNode("anchor", root);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TaskbarAutomationLayoutRootPolicy.SelectUniqueImmediateParent(
                [anchor],
                root,
                static node => node.Parent,
                static (left, right) => ReferenceEquals(left, right)));

        Assert.Equal("The taskbar layout subtree is unavailable or unsafe.", exception.Message);
    }

    [Fact]
    public void LayoutRootPolicy_RejectsAnchorWithoutControlViewParent()
    {
        var root = new AutomationNode("root");
        var anchor = new AutomationNode("anchor");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => TaskbarAutomationLayoutRootPolicy.SelectUniqueImmediateParent(
                [anchor],
                root,
                static node => node.Parent,
                static (left, right) => ReferenceEquals(left, right)));

        Assert.Equal("The taskbar layout subtree is unavailable or unsafe.", exception.Message);
    }

    private sealed class RecordingSubscription : ITaskbarAutomationSubscription
    {
        private readonly List<ApartmentState> apartmentStates = [];
        private readonly List<Action<TaskbarAutomationEventSource>> callbacks = [];
        private readonly List<string> operations = [];
        private readonly List<int> threadIds = [];
        private int subscribeCallCount;
        private int unsubscribeCallCount;
        private bool subscribed;

        internal int FailingSubscribeCall { get; init; }

        internal int FailingUnsubscribeCall { get; init; }

        internal List<ApartmentState> ApartmentStates => apartmentStates;

        internal List<string> Operations => operations;

        internal List<int> ThreadIds => threadIds;

        internal void SignalFromSubscription(int index, TaskbarAutomationEventSource source)
        {
            callbacks[index](source);
        }

        public void Subscribe(
            nint taskbarHandle,
            Action<TaskbarAutomationEventSource> callback)
        {
            Record($"subscribe:{taskbarHandle}");
            callbacks.Add(callback);
            subscribeCallCount++;
            if (subscribeCallCount == FailingSubscribeCall)
            {
                throw new InvalidOperationException("Synthetic subscription failure.");
            }

            subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            Record("unsubscribe");
            subscribed = false;
            unsubscribeCallCount++;
            if (unsubscribeCallCount == FailingUnsubscribeCall)
            {
                throw new InvalidOperationException("Synthetic unsubscription failure.");
            }
        }

        private void Record(string operation)
        {
            operations.Add(operation);
            threadIds.Add(Environment.CurrentManagedThreadId);
            apartmentStates.Add(Thread.CurrentThread.GetApartmentState());
        }
    }

    private sealed record AutomationNode(string Key, AutomationNode? Parent = null);
}
