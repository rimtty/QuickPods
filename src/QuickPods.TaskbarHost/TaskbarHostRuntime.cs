using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using QuickPods.Contracts;
using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Hosting;
using QuickPods.TaskbarHost.Placement;
using QuickPods.TaskbarHost.Presentation;
using QuickPods.TaskbarHost.Runtime;

namespace QuickPods.TaskbarHost;

internal sealed class TaskbarHostRuntime : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(5);

    private readonly ConcurrentQueue<HostStateEnvelope> pendingStates = new();
    private readonly MonotonicSequenceGate stateSequence = new();
    private readonly Process parentProcess;
    private readonly NamedPipeClientStream pipe;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private readonly CancellationTokenSource shutdown = new();
    private readonly string observerExecutablePath;
    private readonly RuntimeNotificationWindow notificationWindow;
    private NativeTaskbarHost? nativeHost;
    private NativeFloatingHost? floatingHost;
    private ObserverProcessSession? observer;
    private TaskbarStateSnapshot currentState;
    private RuntimePlacementIdentity currentPlacement;
    private PixelRect? priorNativeBounds;
    private Task<DiscoveryAttempt>? discoveryTask;
    private Task? readTask;
    private DateTimeOffset nextWatchdogAt;
    private long nextInteractionSequence;
    private long nextObserverGenerationOrdinal;
    private long nextObserverSubscriptionEpoch;
    private long discoveryEpoch;
    private uint observerExplorerProcessId;
    private bool layoutInvalidated;
    private Exception? pipeFailure;
    private bool disposed;

    private TaskbarHostRuntime(string pipeName, int parentProcessId)
    {
        parentProcess = Process.GetProcessById(parentProcessId);
        if (parentProcess.HasExited)
        {
            throw new InvalidOperationException("The QuickPods parent process is no longer running.");
        }

        pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
        reader = new StreamReader(pipe, leaveOpen: true);
        writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        string initialMessage = reader.ReadLine() ??
            throw new InvalidDataException("The QuickPods parent closed before the initial snapshot.");
        HostStateEnvelope initial = QuickPodsProtocolJson.DeserializeState(initialMessage);
        if (!stateSequence.TryAccept(initial.Sequence))
        {
            throw new InvalidDataException("The initial QuickPods state sequence was not accepted.");
        }

        currentState = initial.Snapshot;
        currentPlacement = RuntimePlacementIdentity.Hidden;
        observerExecutablePath = Path.Combine(
            AppContext.BaseDirectory,
            "QuickPods.TaskbarObserver.exe");
        notificationWindow = new RuntimeNotificationWindow();
        nextWatchdogAt = DateTimeOffset.UtcNow + WatchdogInterval;
    }

    internal static int Run(string pipeName, int parentProcessId)
    {
        try
        {
            using var runtime = new TaskbarHostRuntime(pipeName, parentProcessId);
            runtime.RunLoop();
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
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
        shutdown.Cancel();
        DestroySurface();
        DisposeObserver();
        notificationWindow.Dispose();
        try
        {
            readTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }

        writer.Dispose();
        reader.Dispose();
        pipe.Dispose();
        parentProcess.Dispose();
        shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

    private void RunLoop()
    {
        if (IsSurfaceRequested)
        {
            ApplyPlacement(DiscoverPlacement());
        }

        readTask = ReadStatesAsync(shutdown.Token);

        while (!shutdown.IsCancellationRequested && !parentProcess.HasExited)
        {
            ThrowIfPipeFailed();
            DrainStates();
            _ = notificationWindow.PumpMessages();
            _ = nativeHost?.PumpMessages();
            _ = floatingHost?.PumpMessages();
            if (notificationWindow.TryConsumeTaskbarCreated() && IsSurfaceRequested)
            {
                DestroySurface();
                DisposeObserver();
                InvalidateLayout();
            }

            DrainObserver();

            if (layoutInvalidated && IsSurfaceRequested)
            {
                DestroySurface();
                StartDiscoveryIfNeeded();
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now >= nextWatchdogAt)
            {
                nextWatchdogAt = now + WatchdogInterval;
                if (IsSurfaceRequested)
                {
                    StartDiscoveryIfNeeded();
                }
            }

            if (discoveryTask is { IsCompleted: true } completedDiscovery)
            {
                discoveryTask = null;
                DiscoveryAttempt attempt = completedDiscovery.GetAwaiter().GetResult();
                if (!IsSurfaceRequested)
                {
                    layoutInvalidated = false;
                    continue;
                }

                if (attempt.Epoch != discoveryEpoch)
                {
                    StartDiscoveryIfNeeded();
                    continue;
                }

                TaskbarRuntimePlacement placement = attempt.Placement;
                if (layoutInvalidated || placement.Identity != currentPlacement)
                {
                    ApplyPlacement(placement);
                }

                layoutInvalidated = false;
            }

            if (readTask.IsCompleted)
            {
                readTask.GetAwaiter().GetResult();
                return;
            }

            Thread.Sleep(8);
        }
    }

    private async Task ReadStatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    return;
                }

                pendingStates.Enqueue(QuickPodsProtocolJson.DeserializeState(message));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            pipeFailure = exception;
        }
    }

    private void DrainStates()
    {
        TaskbarStateSnapshot? accepted = null;
        while (pendingStates.TryDequeue(out HostStateEnvelope? envelope))
        {
            if (stateSequence.TryAccept(envelope.Sequence))
            {
                accepted = envelope.Snapshot;
            }
        }

        if (accepted is null)
        {
            return;
        }

        bool wasSurfaceRequested = IsSurfaceRequested;
        currentState = accepted;
        if (!IsSurfaceRequested)
        {
            DestroySurface();
            DisposeObserver();
            currentPlacement = RuntimePlacementIdentity.Hidden;
            layoutInvalidated = false;
            return;
        }

        if (!wasSurfaceRequested)
        {
            InvalidateLayout();
            return;
        }

        ApplyStateToSurface();
    }

    private void ApplyPlacement(TaskbarRuntimePlacement placement)
    {
        DestroySurface();
        currentPlacement = placement.Identity;
        if (placement.Route.Surface == TaskbarPresentationSurface.Native &&
            placement.Discovery.Snapshot is LiveTaskbarSnapshot snapshot &&
            placement.Route.Bounds is PixelRect nativeBounds)
        {
            EnsureObserver(snapshot.ExplorerProcessId);
            nativeHost = new NativeTaskbarHost();
            nativeHost.Interaction += ForwardInteraction;
            nativeHost.LayoutInvalidated += OnLayoutInvalidated;
            nativeHost.CreateHidden(
                snapshot.TaskbarHandle,
                nativeBounds,
                snapshot.Dpi,
                StateFor(TaskbarSurfaceMode.Native));
            nativeHost.Show();
            priorNativeBounds = nativeBounds;
            return;
        }

        if (placement.Route.Surface == TaskbarPresentationSurface.Floating &&
            placement.Discovery.Snapshot is LiveTaskbarSnapshot floatingSnapshot &&
            placement.Route.Bounds is PixelRect floatingBounds &&
            placement.Route.VerifiedWorkArea is PixelRect workArea)
        {
            EnsureObserver(floatingSnapshot.ExplorerProcessId);
            floatingHost = new NativeFloatingHost();
            floatingHost.Interaction += ForwardInteraction;
            floatingHost.LayoutInvalidated += OnLayoutInvalidated;
            floatingHost.CreateHidden(
                floatingBounds,
                workArea,
                placement.Route.Dpi,
                StateFor(TaskbarSurfaceMode.Floating));
            floatingHost.Show();
            return;
        }

        DisposeObserver();
    }

    private void ApplyStateToSurface()
    {
        nativeHost?.SetState(StateFor(TaskbarSurfaceMode.Native));
        floatingHost?.SetState(StateFor(TaskbarSurfaceMode.Floating));
    }

    private TaskbarStateSnapshot StateFor(TaskbarSurfaceMode mode) =>
        currentState with { SurfaceMode = mode };

    private bool IsSurfaceRequested => currentState.SurfaceMode != TaskbarSurfaceMode.Hidden;

    private void ForwardInteraction(HostInteractionEnvelope interaction)
    {
        if (nextInteractionSequence == long.MaxValue)
        {
            throw new InvalidOperationException("The taskbar-host interaction sequence was exhausted.");
        }

        var normalized = new HostInteractionEnvelope(
            QuickPodsProtocol.Version,
            nextInteractionSequence++,
            interaction.Kind,
            interaction.VolumePercent,
            interaction.Anchor);
        writer.WriteLine(QuickPodsProtocolJson.Serialize(normalized));
    }

    private void OnLayoutInvalidated(NativeLayoutInvalidationReason reason)
    {
        _ = reason;
        InvalidateLayout();
    }

    private void DrainObserver()
    {
        if (observer is null)
        {
            return;
        }

        bool invalidate = observer.Failure is not null || observer.IsDisconnected;
        while (observer.TryDequeue(out ObserverInvalidationBatch? batch))
        {
            if (batch is null || batch.Source == ObserverSourceClassification.Owned)
            {
                continue;
            }

            ObserverInvalidationKind actionable = batch.Kinds & ~ObserverInvalidationKind.Ready;
            invalidate |= actionable != ObserverInvalidationKind.None;
        }

        if (!invalidate)
        {
            return;
        }

        DestroySurface();
        DisposeObserver();
        InvalidateLayout();
        StartDiscoveryIfNeeded();
    }

    private void EnsureObserver(uint explorerProcessId)
    {
        if (explorerProcessId == 0)
        {
            throw new InvalidOperationException("The verified Explorer generation is unavailable.");
        }

        if (observer is { IsDisconnected: false } &&
            observerExplorerProcessId == explorerProcessId)
        {
            return;
        }

        DisposeObserver();
        if (nextObserverSubscriptionEpoch == long.MaxValue ||
            nextObserverGenerationOrdinal == long.MaxValue)
        {
            throw new InvalidOperationException("The observer generation sequence was exhausted.");
        }

        observer = ObserverProcessSession.Start(
            observerExecutablePath,
            nextObserverSubscriptionEpoch++,
            nextObserverGenerationOrdinal++);
        observerExplorerProcessId = explorerProcessId;
    }

    private void DisposeObserver()
    {
        observer?.Dispose();
        observer = null;
        observerExplorerProcessId = 0;
    }

    private void StartDiscoveryIfNeeded()
    {
        long epoch = discoveryEpoch;
        discoveryTask ??= Task.Run(() => new DiscoveryAttempt(epoch, DiscoverPlacement()));
    }

    private void InvalidateLayout()
    {
        if (discoveryEpoch == long.MaxValue)
        {
            throw new InvalidOperationException("The taskbar discovery epoch was exhausted.");
        }

        discoveryEpoch++;
        layoutInvalidated = true;
    }

    private TaskbarRuntimePlacement DiscoverPlacement()
    {
        TaskbarDiscoveryResult discovery =
            TaskbarDiscoveryService.DiscoverAsync().GetAwaiter().GetResult();
        TaskbarLayoutObservation? observation = TaskbarObservationAdapter.Create(discovery);
        TaskbarPlacementResult placement = SafeRegionPlanner.Calculate(
            observation,
            TaskbarPlacementOptions.Default);
        TaskbarPresentationRoute route = TaskbarPresentationRouter.Select(
            discovery,
            placement,
            priorNativeBounds);
        RuntimePlacementIdentity identity = RuntimePlacementIdentity.From(discovery, route);
        return new(discovery, route, identity);
    }

    private void DestroySurface()
    {
        if (nativeHost is not null)
        {
            nativeHost.Interaction -= ForwardInteraction;
            nativeHost.LayoutInvalidated -= OnLayoutInvalidated;
            nativeHost.Dispose();
            nativeHost = null;
        }

        if (floatingHost is not null)
        {
            floatingHost.Interaction -= ForwardInteraction;
            floatingHost.LayoutInvalidated -= OnLayoutInvalidated;
            floatingHost.Dispose();
            floatingHost = null;
        }

        currentPlacement = RuntimePlacementIdentity.Hidden;
    }

    private void ThrowIfPipeFailed()
    {
        if (pipeFailure is { } failure)
        {
            throw new IOException("The QuickPods parent pipe failed.", failure);
        }
    }

    private readonly record struct TaskbarRuntimePlacement(
        TaskbarDiscoveryResult Discovery,
        TaskbarPresentationRoute Route,
        RuntimePlacementIdentity Identity);

    private readonly record struct DiscoveryAttempt(
        long Epoch,
        TaskbarRuntimePlacement Placement);

    private readonly record struct RuntimePlacementIdentity(
        TaskbarPresentationSurface Surface,
        nint TaskbarHandle,
        uint ExplorerProcessId,
        PixelRect? Bounds,
        PixelRect? WorkArea,
        uint Dpi)
    {
        internal static RuntimePlacementIdentity Hidden { get; } = new(
            TaskbarPresentationSurface.Hidden,
            nint.Zero,
            0,
            null,
            null,
            0);

        internal static RuntimePlacementIdentity From(
            TaskbarDiscoveryResult discovery,
            TaskbarPresentationRoute route)
        {
            LiveTaskbarSnapshot? snapshot = discovery.Snapshot;
            return new(
                route.Surface,
                snapshot?.TaskbarHandle ?? nint.Zero,
                snapshot?.ExplorerProcessId ?? 0,
                route.Bounds,
                route.VerifiedWorkArea,
                route.Dpi);
        }
    }
}
