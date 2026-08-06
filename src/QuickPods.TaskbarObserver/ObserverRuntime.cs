using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using QuickPods.Contracts;

namespace QuickPods.TaskbarObserver;

internal sealed class ObserverRuntime : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(5);

    private readonly Process parentProcess;
    private readonly NamedPipeClientStream pipe;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private readonly ObserverSessionRequest request;
    private readonly ObserverSubscription subscription;
    private long nextSequence;
    private bool disposed;

    private ObserverRuntime(string pipeName, int parentProcessId)
    {
        parentProcess = Process.GetProcessById(parentProcessId);
        if (parentProcess.HasExited)
        {
            throw new InvalidOperationException("The QuickPods taskbar host is no longer running.");
        }

        pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
        reader = new StreamReader(pipe, leaveOpen: true);
        writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        string message = reader.ReadLine() ??
            throw new InvalidDataException("The taskbar host closed before the observer handshake.");
        request = QuickPodsProtocolJson.DeserializeObserverSession(message);
        subscription = ObserverSubscription.Start(parentProcessId, WorkerTimeout);
    }

    internal static int Run(string pipeName, int parentProcessId)
    {
        try
        {
            using var runtime = new ObserverRuntime(pipeName, parentProcessId);
            return runtime.RunLoop();
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            COMException or
            ElementNotAvailableException or
            System.ComponentModel.Win32Exception)
        {
            return 6;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        subscription.Dispose();
        writer.Dispose();
        reader.Dispose();
        pipe.Dispose();
        parentProcess.Dispose();
        GC.SuppressFinalize(this);
    }

    private int RunLoop()
    {
        WriteBatch(
            ObserverInvalidationKind.Ready,
            ObserverSourceClassification.Unknown);
        while (!parentProcess.HasExited && subscription.IsAlive)
        {
            if (subscription.Failure is { } failure)
            {
                _ = failure;
                WriteBatch(
                    ObserverInvalidationKind.ObserverFaulted,
                    ObserverSourceClassification.Unknown);
                return 6;
            }

            if (!subscription.TryTake(out ObserverSignal first, TimeSpan.FromMilliseconds(250)))
            {
                continue;
            }

            ObserverInvalidationKind kinds = first.Kind;
            ObserverSourceClassification source = first.Source;
            while (subscription.TryTake(out ObserverSignal next, TimeSpan.Zero))
            {
                kinds |= next.Kind;
                source = Stronger(source, next.Source);
            }

            WriteBatch(kinds, source);
            if ((kinds & ObserverInvalidationKind.ExplorerGenerationChanged) != 0)
            {
                return 7;
            }
        }

        return parentProcess.HasExited ? 0 : 6;
    }

    private void WriteBatch(
        ObserverInvalidationKind kinds,
        ObserverSourceClassification source)
    {
        if (nextSequence == long.MaxValue)
        {
            throw new InvalidOperationException("The observer sequence was exhausted.");
        }

        var batch = new ObserverInvalidationBatch(
            QuickPodsProtocol.Version,
            nextSequence++,
            request.SubscriptionEpoch,
            request.GenerationOrdinal,
            kinds,
            source);
        writer.WriteLine(QuickPodsProtocolJson.Serialize(batch));
    }

    private static ObserverSourceClassification Stronger(
        ObserverSourceClassification left,
        ObserverSourceClassification right)
    {
        if (left == ObserverSourceClassification.Unknown ||
            right == ObserverSourceClassification.Unknown)
        {
            return ObserverSourceClassification.Unknown;
        }

        return left == ObserverSourceClassification.External ||
               right == ObserverSourceClassification.External
            ? ObserverSourceClassification.External
            : ObserverSourceClassification.Owned;
    }

    private sealed class ObserverSubscription : IDisposable
    {
        private readonly BlockingCollection<ObserverSignal> signals = [];
        private readonly ManualResetEventSlim stop = new(false);
        private readonly TaskCompletionSource<bool> ready = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread worker;
        private readonly int ownedProcessId;
        private Exception? failure;
        private bool disposed;

        private ObserverSubscription(int ownedProcessId)
        {
            this.ownedProcessId = ownedProcessId;
            worker = new Thread(WorkerEntry)
            {
                IsBackground = true,
                Name = "QuickPods taskbar observer UIA MTA",
            };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }

        internal bool IsAlive => worker.IsAlive;

        internal Exception? Failure => Volatile.Read(ref failure);

        internal static ObserverSubscription Start(int ownedProcessId, TimeSpan timeout)
        {
            var subscription = new ObserverSubscription(ownedProcessId);
            try
            {
                subscription.ready.Task.WaitAsync(timeout).GetAwaiter().GetResult();
                return subscription;
            }
            catch
            {
                subscription.Dispose();
                throw;
            }
        }

        internal bool TryTake(out ObserverSignal signal, TimeSpan timeout) =>
            signals.TryTake(out signal, timeout);

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            stop.Set();
            if (worker.Join(WorkerTimeout))
            {
                signals.Dispose();
                stop.Dispose();
            }
        }

        private void WorkerEntry()
        {
            bool registrationAttempted = false;
            try
            {
                if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
                {
                    throw new InvalidOperationException("The observer worker is not MTA.");
                }

                ObserverNativeMethods.PrimaryTaskbarIdentity identity =
                    ObserverNativeMethods.FindPrimaryTaskbar();
                AutomationElement root = AutomationElement.FromHandle(identity.WindowHandle) ??
                    throw new InvalidOperationException("The taskbar UI Automation root is unavailable.");

                StructureChangedEventHandler structureHandler = (sender, _) =>
                    Signal(sender, ObserverInvalidationKind.StructureChanged, placementButtonsOnly: false);
                AutomationPropertyChangedEventHandler propertyHandler = (sender, eventArgs) =>
                    Signal(
                        sender,
                        eventArgs.Property == AutomationElement.BoundingRectangleProperty
                            ? ObserverInvalidationKind.BoundingRectangleChanged
                            : ObserverInvalidationKind.IsOffscreenChanged,
                        placementButtonsOnly: true);
                var cache = new CacheRequest
                {
                    AutomationElementMode = AutomationElementMode.None,
                    TreeFilter = Automation.ControlViewCondition,
                    TreeScope = TreeScope.Element,
                };
                cache.Add(AutomationElement.ProcessIdProperty);
                cache.Add(AutomationElement.ControlTypeProperty);
                using IDisposable activation = cache.Activate();
                registrationAttempted = true;
                Automation.AddStructureChangedEventHandler(root, TreeScope.Subtree, structureHandler);
                Automation.AddAutomationPropertyChangedEventHandler(
                    root,
                    TreeScope.Subtree,
                    propertyHandler,
                    AutomationElement.BoundingRectangleProperty,
                    AutomationElement.IsOffscreenProperty);
                ready.TrySetResult(true);

                while (!stop.Wait(TimeSpan.FromMilliseconds(250)))
                {
                    ObserverNativeMethods.PrimaryTaskbarIdentity current =
                        ObserverNativeMethods.FindPrimaryTaskbar();
                    if (current != identity)
                    {
                        signals.Add(new(
                            ObserverInvalidationKind.ExplorerGenerationChanged,
                            ObserverSourceClassification.External));
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                Volatile.Write(ref failure, exception);
                ready.TrySetException(exception);
            }
            finally
            {
                if (registrationAttempted)
                {
                    try
                    {
                        Automation.RemoveAllEventHandlers();
                    }
                    catch (Exception exception)
                    {
                        Volatile.Write(ref failure, exception);
                    }
                }
            }
        }

        private void Signal(
            object sender,
            ObserverInvalidationKind kind,
            bool placementButtonsOnly)
        {
            int processId = 0;
            bool controlTypeKnown = false;
            bool isButton = false;
            try
            {
                if (sender is AutomationElement element)
                {
                    object processValue = element.GetCachedPropertyValue(
                        AutomationElement.ProcessIdProperty,
                        true);
                    if (processValue is int senderProcessId)
                    {
                        processId = senderProcessId;
                    }

                    object typeValue = element.GetCachedPropertyValue(
                        AutomationElement.ControlTypeProperty,
                        true);
                    controlTypeKnown = typeValue is ControlType;
                    isButton = typeValue is ControlType type && type == ControlType.Button;
                }
            }
            catch (Exception)
            {
                processId = 0;
                controlTypeKnown = false;
            }

            if (placementButtonsOnly && controlTypeKnown && !isButton)
            {
                return;
            }

            ObserverSourceClassification source = processId == ownedProcessId
                ? ObserverSourceClassification.Owned
                : processId != 0
                    ? ObserverSourceClassification.External
                    : ObserverSourceClassification.Unknown;
            if (source == ObserverSourceClassification.Owned &&
                kind != ObserverInvalidationKind.BoundingRectangleChanged)
            {
                return;
            }

            try
            {
                signals.Add(new(kind, source));
            }
            catch (InvalidOperationException) when (signals.IsAddingCompleted)
            {
            }
        }
    }

    private readonly record struct ObserverSignal(
        ObserverInvalidationKind Kind,
        ObserverSourceClassification Source);
}
