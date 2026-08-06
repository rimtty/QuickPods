using System.Diagnostics;
using System.IO.Pipes;
using System.Threading.Channels;
using QuickPods.Contracts;

namespace QuickPods.Infrastructure.Runtime;

public sealed class TaskbarHostProcessManager : IAsyncDisposable
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan StableConnectionDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(2);

    private readonly string executablePath;
    private readonly TaskbarHostSupervisor supervisor;
    private readonly TimeProvider timeProvider;
    private readonly Channel<TaskbarStateSnapshot> pendingSnapshots =
        Channel.CreateBounded<TaskbarStateSnapshot>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource shutdown = new();
    private readonly object stateLock = new();
    private readonly MonotonicSequenceGate interactionSequence = new();
    private TaskbarStateSnapshot latestSnapshot =
        new(TaskbarSurfaceMode.Hidden, 0, false, null);
    private Task? runTask;
    private long nextStateSequence;
    private bool disposed;

    public TaskbarHostProcessManager(
        string executablePath,
        TaskbarHostSupervisor? supervisor = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        this.executablePath = Path.GetFullPath(executablePath);
        this.supervisor = supervisor ?? new TaskbarHostSupervisor();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event EventHandler<HostInteractionEnvelope>? InteractionReceived;

    public event EventHandler<TaskbarHostSupervisorState>? StateChanged;

    public TaskbarHostSupervisorState State => supervisor.State;

    public void Start(TaskbarStateSnapshot initialSnapshot)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(initialSnapshot);
        if (runTask is not null)
        {
            throw new InvalidOperationException("The taskbar-host manager has already been started.");
        }

        SetLatestSnapshot(initialSnapshot);
        runTask = RunAsync(shutdown.Token);
    }

    public void Publish(TaskbarStateSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        SetLatestSnapshot(snapshot);
        _ = pendingSnapshots.Writer.TryWrite(snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        shutdown.Cancel();
        pendingSnapshots.Writer.TryComplete();
        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
            }
        }

        shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested &&
               supervisor.State.Lifecycle != TaskbarHostLifecycle.DisabledForSession)
        {
            Process? process = null;
            try
            {
                string pipeName = $"QuickPods.{Guid.NewGuid():N}";
                await using var pipe = CreateServer(pipeName);
                RecordState(supervisor.RecordStart);
                process = StartHostProcess(pipeName);

                using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectionTimeout.CancelAfter(ConnectionTimeout);
                await pipe.WaitForConnectionAsync(connectionTimeout.Token).ConfigureAwait(false);
                RecordState(supervisor.RecordConnected);

                interactionSequence.Reset();
                DateTimeOffset connectedAt = timeProvider.GetUtcNow();
                await RunConnectedAsync(pipe, process, cancellationToken).ConfigureAwait(false);
                if (timeProvider.GetUtcNow() - connectedAt >= StableConnectionDuration)
                {
                    RecordState(supervisor.RecordStable);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    RecordState(() => supervisor.RecordUnexpectedExit(timeProvider.GetUtcNow()));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                RecordState(() => supervisor.RecordUnexpectedExit(timeProvider.GetUtcNow()));
            }
            finally
            {
                await StopOwnedProcessAsync(process).ConfigureAwait(false);
            }

            if (supervisor.State.Lifecycle == TaskbarHostLifecycle.DisabledForSession)
            {
                break;
            }

            DateTimeOffset retryAfter = supervisor.State.RetryAfter ?? timeProvider.GetUtcNow();
            TimeSpan delay = retryAfter - timeProvider.GetUtcNow();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            RecordState(supervisor.RecordStopped);
        }
    }

    private async Task RunConnectedAsync(
        NamedPipeServerStream pipe,
        Process process,
        CancellationToken cancellationToken)
    {
        using var connectionShutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        await WriteSnapshotAsync(writer, GetLatestSnapshot(), connectionShutdown.Token)
            .ConfigureAwait(false);
        Task receiveTask = ReceiveInteractionsAsync(reader, connectionShutdown.Token);
        Task sendTask = SendSnapshotsAsync(writer, connectionShutdown.Token);
        Task exitTask = process.WaitForExitAsync(connectionShutdown.Token);

        Task completed = await Task.WhenAny(receiveTask, sendTask, exitTask).ConfigureAwait(false);
        connectionShutdown.Cancel();
        try
        {
            await completed.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (connectionShutdown.IsCancellationRequested)
        {
        }

        await ObserveCancellationAsync(receiveTask).ConfigureAwait(false);
        await ObserveCancellationAsync(sendTask).ConfigureAwait(false);
        await ObserveCancellationAsync(exitTask).ConfigureAwait(false);
    }

    private async Task ReceiveInteractionsAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return;
            }

            HostInteractionEnvelope interaction = QuickPodsProtocolJson.DeserializeInteraction(message);
            if (interactionSequence.TryAccept(interaction.Sequence))
            {
                InteractionReceived?.Invoke(this, interaction);
            }
        }
    }

    private async Task SendSnapshotsAsync(
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        await foreach (TaskbarStateSnapshot snapshot in
                       pendingSnapshots.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            await WriteSnapshotAsync(writer, snapshot, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteSnapshotAsync(
        StreamWriter writer,
        TaskbarStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        long sequence = Interlocked.Increment(ref nextStateSequence) - 1;
        var envelope = new HostStateEnvelope(QuickPodsProtocol.Version, sequence, snapshot);
        await writer.WriteLineAsync(QuickPodsProtocolJson.Serialize(envelope).AsMemory(), cancellationToken)
            .ConfigureAwait(false);
    }

    private Process StartHostProcess(string pipeName)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The QuickPods taskbar-host executable was not found.", executablePath);
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--pipe-name");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Process.Start(startInfo) ??
            throw new InvalidOperationException("The QuickPods taskbar-host process did not start.");
    }

    private static NamedPipeServerStream CreateServer(string pipeName) =>
        new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static async Task StopOwnedProcessAsync(Process? process)
    {
        if (process is null)
        {
            return;
        }

        using (process)
        {
            if (process.HasExited)
            {
                return;
            }

            using var timeout = new CancellationTokenSource(GracefulExitTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task ObserveCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetLatestSnapshot(TaskbarStateSnapshot snapshot)
    {
        lock (stateLock)
        {
            latestSnapshot = snapshot;
        }
    }

    private TaskbarStateSnapshot GetLatestSnapshot()
    {
        lock (stateLock)
        {
            return latestSnapshot;
        }
    }

    private void RecordState(Action transition)
    {
        transition();
        StateChanged?.Invoke(this, supervisor.State);
    }
}
