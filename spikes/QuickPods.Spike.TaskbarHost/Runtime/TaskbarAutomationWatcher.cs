using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Windows.Automation;

namespace QuickPods.Spike.TaskbarHost.Runtime;

[SupportedOSPlatform("windows")]
internal sealed class TaskbarAutomationWatcher : IDisposable
{
    internal static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(5);

    private readonly BlockingCollection<WatcherCommand> commands = [];
    private readonly Func<ITaskbarAutomationSubscription> subscriptionFactory;
    private readonly TaskbarAutomationInvalidationSignal signal;
    private readonly TaskCompletionSource<bool> workerReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread worker;
    private long activeSubscriptionEpoch;
    private long nextSubscriptionEpoch;
    private bool disposed;
    private nint taskbarHandle;

    private TaskbarAutomationWatcher(
        TaskbarAutomationInvalidationSignal signal,
        Func<ITaskbarAutomationSubscription> subscriptionFactory)
    {
        this.signal = signal;
        this.subscriptionFactory = subscriptionFactory;
        worker = new Thread(WorkerEntry)
        {
            IsBackground = true,
            Name = "QuickPods.TaskbarAutomation.WatcherMTA",
        };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        WaitFor(workerReady.Task);
    }

    internal nint TaskbarHandle => taskbarHandle;

    internal static TaskbarAutomationWatcher Start(
        nint taskbarHandle,
        TaskbarAutomationInvalidationSignal signal)
    {
        return Start(taskbarHandle, signal, static () => new UiaSubscription());
    }

    internal static TaskbarAutomationWatcher Start(
        nint taskbarHandle,
        TaskbarAutomationInvalidationSignal signal,
        Func<ITaskbarAutomationSubscription> subscriptionFactory)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(subscriptionFactory);
        ArgumentOutOfRangeException.ThrowIfEqual(taskbarHandle, nint.Zero);

        var watcher = new TaskbarAutomationWatcher(signal, subscriptionFactory);
        try
        {
            watcher.Send(WatcherCommandKind.Subscribe, taskbarHandle);
            watcher.taskbarHandle = taskbarHandle;
            return watcher;
        }
        catch (Exception startFailure)
        {
            try
            {
                watcher.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "UI Automation watcher startup and cleanup both failed.",
                    startFailure,
                    cleanupFailure);
            }

            throw;
        }
    }

    internal bool ReplaceTaskbar(nint replacementTaskbarHandle)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfEqual(replacementTaskbarHandle, nint.Zero);
        if (replacementTaskbarHandle == taskbarHandle)
        {
            return false;
        }

        Send(WatcherCommandKind.Replace, replacementTaskbarHandle);
        taskbarHandle = replacementTaskbarHandle;
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SendCore(WatcherCommandKind.Stop, nint.Zero);
        if (!worker.Join(OperationTimeout))
        {
            throw new TimeoutException("The UI Automation watcher MTA did not stop in time.");
        }

        commands.Dispose();
    }

    private void Send(WatcherCommandKind kind, nint handle)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SendCore(kind, handle);
    }

    private void SendCore(WatcherCommandKind kind, nint handle)
    {
        var command = new WatcherCommand(kind, handle);
        commands.Add(command);
        WaitFor(command.Completion.Task);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "No exception may escape the dedicated UI Automation thread entry point.")]
    private void WorkerEntry()
    {
        ITaskbarAutomationSubscription? subscription = null;
        try
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
            {
                throw new InvalidOperationException("The UI Automation watcher was not initialized as MTA.");
            }

            subscription = subscriptionFactory();
            workerReady.TrySetResult(true);
            foreach (WatcherCommand command in commands.GetConsumingEnumerable())
            {
                bool stop = command.Kind == WatcherCommandKind.Stop;
                try
                {
                    switch (command.Kind)
                    {
                        case WatcherCommandKind.Subscribe:
                            Subscribe(subscription, command.TaskbarHandle);
                            break;
                        case WatcherCommandKind.Replace:
                            Interlocked.Exchange(ref activeSubscriptionEpoch, 0);
                            subscription.Unsubscribe();
                            Subscribe(subscription, command.TaskbarHandle);
                            break;
                        case WatcherCommandKind.Stop:
                            Interlocked.Exchange(ref activeSubscriptionEpoch, 0);
                            subscription.Unsubscribe();
                            break;
                        default:
                            throw new InvalidOperationException("Unknown UI Automation watcher command.");
                    }

                    command.Completion.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    Interlocked.Exchange(ref activeSubscriptionEpoch, 0);
                    command.Completion.TrySetException(exception);
                }

                if (stop)
                {
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            workerReady.TrySetException(exception);
        }
        finally
        {
            try
            {
                subscription?.Unsubscribe();
            }
            catch (Exception)
            {
                // A command already reports the failure. The background-thread
                // boundary must still terminate without leaking an exception.
            }
        }
    }

    private void Subscribe(ITaskbarAutomationSubscription subscription, nint handle)
    {
        long epoch = Interlocked.Increment(ref nextSubscriptionEpoch);
        Interlocked.Exchange(ref activeSubscriptionEpoch, epoch);
        subscription.Subscribe(
            handle,
            source =>
            {
                if (Interlocked.Read(ref activeSubscriptionEpoch) == epoch)
                {
                    signal.Signal(source);
                }
            });
    }

    private static void WaitFor(Task task)
    {
        task.WaitAsync(OperationTimeout).GetAwaiter().GetResult();
    }

    private enum WatcherCommandKind
    {
        Subscribe,
        Replace,
        Stop,
    }

    private sealed class WatcherCommand(WatcherCommandKind kind, nint taskbarHandle)
    {
        internal WatcherCommandKind Kind { get; } = kind;

        internal nint TaskbarHandle { get; } = taskbarHandle;

        internal TaskCompletionSource<bool> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class UiaSubscription : ITaskbarAutomationSubscription
    {
        private static readonly ConcurrentBag<Delegate> RetiredHandlers = [];

        private AutomationElement? layoutRoot;
        private AutomationElement? taskbarRoot;
        private AutomationPropertyChangedEventHandler? propertyHandler;
        private StructureChangedEventHandler? layoutStructureHandler;
        private bool eventRegistrationAttempted;

        public void Subscribe(
            nint taskbarHandle,
            Action<TaskbarAutomationEventSource> callback)
        {
            if (HasSubscriptionState())
            {
                throw new InvalidOperationException("A UI Automation root is already subscribed.");
            }

            AutomationElement candidate = AutomationElement.FromHandle(taskbarHandle) ??
                throw new InvalidOperationException("The taskbar UI Automation root is unavailable.");
            AutomationElementCollection anchorMatches = candidate.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.AutomationIdProperty,
                    TaskbarAutomationLayoutRootPolicy.AnchorAutomationId,
                    PropertyConditionFlags.None));
            AutomationElement candidateLayoutRoot =
                TaskbarAutomationLayoutRootPolicy.SelectUniqueImmediateParent(
                    [.. anchorMatches.Cast<AutomationElement>()],
                    candidate,
                    static element => TreeWalker.ControlViewWalker.GetParent(element),
                    static (left, right) => Automation.Compare(left, right));

            propertyHandler = (sender, eventArgs) => SignalCachedSender(
                sender,
                callback,
                eventArgs.Property == AutomationElement.BoundingRectangleProperty
                    ? TaskbarAutomationEventKind.BoundingRectangleChanged
                    : TaskbarAutomationEventKind.IsOffscreenChanged,
                placementButtonsOnly: true);
            layoutStructureHandler = (sender, _) => SignalCachedSender(
                sender,
                callback,
                TaskbarAutomationEventKind.StructureChanged,
                placementButtonsOnly: false);

            var cache = new CacheRequest
            {
                AutomationElementMode = AutomationElementMode.None,
                TreeFilter = Automation.ControlViewCondition,
                // Cache only the event sender. Caching its subtree on every
                // callback can recursively query the hosted HWND and delay removal.
                TreeScope = TreeScope.Element,
            };
            cache.Add(AutomationElement.NativeWindowHandleProperty);
            cache.Add(AutomationElement.ProcessIdProperty);
            cache.Add(AutomationElement.ControlTypeProperty);
            using IDisposable activation = cache.Activate();
            taskbarRoot = candidate;
            layoutRoot = candidateLayoutRoot;
            try
            {
                // An Add* call can fail after the provider has accepted the
                // callback. Treat every attempted registration as potentially
                // live so partial startup always executes same-MTA RemoveAll.
                eventRegistrationAttempted = true;
                Automation.AddStructureChangedEventHandler(
                    candidateLayoutRoot,
                    TreeScope.Subtree,
                    layoutStructureHandler);
                Automation.AddAutomationPropertyChangedEventHandler(
                    candidate,
                    TreeScope.Subtree,
                    propertyHandler,
                    AutomationElement.BoundingRectangleProperty,
                    AutomationElement.IsOffscreenProperty);
            }
            catch (Exception subscriptionFailure)
            {
                try
                {
                    RemoveHandlers();
                }
                catch (Exception cleanupFailure)
                {
                    ClearRoots();
                    throw new AggregateException(
                        "UI Automation subscription and cleanup both failed.",
                        subscriptionFailure,
                        cleanupFailure);
                }

                ClearRoots();
                throw;
            }
        }

        public void Unsubscribe()
        {
            if (HasSubscriptionState())
            {
                try
                {
                    RemoveHandlers();
                }
                finally
                {
                    ClearRoots();
                }
            }
        }

        [SuppressMessage(
            "Design",
            "CA1031:Do not catch general exception types",
            Justification = "A UIA callback may outlive its provider; it must only fail closed into the atomic signal.")]
        private static void SignalCachedSender(
            object sender,
            Action<TaskbarAutomationEventSource> callback,
            TaskbarAutomationEventKind kind,
            bool placementButtonsOnly)
        {
            int nativeWindowHandle = 0;
            int processId = 0;
            try
            {
                if (sender is AutomationElement element)
                {
                    object controlTypeValue = element.GetCachedPropertyValue(
                        AutomationElement.ControlTypeProperty,
                        true);
                    bool controlTypeKnown =
                        !ReferenceEquals(controlTypeValue, AutomationElement.NotSupported) &&
                        controlTypeValue is ControlType;
                    bool isPlacementButton =
                        controlTypeValue is ControlType controlType &&
                        controlType == ControlType.Button;
                    if (!TaskbarAutomationEventPolicy.ShouldSignal(
                            placementButtonsOnly,
                            controlTypeKnown,
                            isPlacementButton))
                    {
                        return;
                    }

                    object value = element.GetCachedPropertyValue(
                        AutomationElement.NativeWindowHandleProperty,
                        true);
                    if (!ReferenceEquals(value, AutomationElement.NotSupported) && value is int handle)
                    {
                        nativeWindowHandle = handle;
                    }

                    value = element.GetCachedPropertyValue(
                        AutomationElement.ProcessIdProperty,
                        true);
                    if (!ReferenceEquals(value, AutomationElement.NotSupported) && value is int senderProcessId)
                    {
                        processId = senderProcessId;
                    }
                }
            }
            catch (Exception)
            {
                // Unknown sender identity is signaled, never ignored.
            }

            callback(new(nativeWindowHandle, processId, kind));
        }

        private void RemoveHandlers()
        {
            try
            {
                if (eventRegistrationAttempted)
                {
                    // This spike is the sole owner of UIA event subscriptions in
                    // this process. A process-wide removal avoids provider element
                    // identity round trips during Explorer teardown and executes
                    // on the same dedicated MTA that registered the handlers.
                    Automation.RemoveAllEventHandlers();
                }
            }
            finally
            {
                eventRegistrationAttempted = false;
                Retire(ref propertyHandler);
                Retire(ref layoutStructureHandler);
            }
        }

        private void ClearRoots()
        {
            layoutRoot = null;
            taskbarRoot = null;
        }

        private bool HasSubscriptionState()
        {
            return layoutRoot is not null ||
                taskbarRoot is not null ||
                propertyHandler is not null ||
                layoutStructureHandler is not null ||
                eventRegistrationAttempted;
        }

        private static void Retire<TDelegate>(ref TDelegate? handler)
            where TDelegate : Delegate
        {
            if (handler is null)
            {
                return;
            }

            // UIA can deliver an event after removal returns. Process-lifetime
            // roots keep every unmanaged callback target valid; retired watcher
            // epochs make those late callbacks atomic no-ops.
            RetiredHandlers.Add(handler);
            handler = null;
        }
    }
}

internal static class TaskbarAutomationEventPolicy
{
    /// <summary>
    /// Property notifications are relevant only when they come from the Button
    /// elements used by placement discovery. Windows 11 also raises recurring
    /// bounds notifications for structural Pane containers while UIA is queried;
    /// those containers are not placement obstacles. Unknown sender types still
    /// signal so a cache/provider failure remains fail closed. Structure changes
    /// are always signaled because they can add or remove placement buttons.
    /// </summary>
    internal static bool ShouldSignal(
        bool placementButtonsOnly,
        bool controlTypeKnown,
        bool isPlacementButton) =>
        !placementButtonsOnly || !controlTypeKnown || isPlacementButton;
}

internal interface ITaskbarAutomationSubscription
{
    void Subscribe(
        nint taskbarHandle,
        Action<TaskbarAutomationEventSource> callback);

    void Unsubscribe();
}

internal static class TaskbarAutomationLayoutRootPolicy
{
    internal const string AnchorAutomationId = "StartButton";

    internal static TNode SelectUniqueImmediateParent<TNode>(
        IReadOnlyCollection<TNode> anchors,
        TNode propertyRoot,
        Func<TNode, TNode?> getControlViewParent,
        Func<TNode, TNode, bool> areSame)
        where TNode : class
    {
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(propertyRoot);
        ArgumentNullException.ThrowIfNull(getControlViewParent);
        ArgumentNullException.ThrowIfNull(areSame);

        if (anchors.Count != 1)
        {
            throw new InvalidOperationException(
                "The taskbar layout anchor is unavailable or ambiguous.");
        }

        TNode anchor = anchors.Single();
        TNode? immediateParent = getControlViewParent(anchor);
        if (immediateParent is null || areSame(propertyRoot, immediateParent))
        {
            throw new InvalidOperationException(
                "The taskbar layout subtree is unavailable or unsafe.");
        }

        return immediateParent;
    }
}
