using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using QuickPods.Contracts;

namespace QuickPods.TaskbarHost.Runtime;

internal sealed class ObserverProcessSession : IDisposable
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentQueue<ObserverInvalidationBatch> pending = new();
    private readonly MonotonicSequenceGate sequenceGate = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly NamedPipeServerStream pipe;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private readonly Process process;
    private readonly OwnedProcessJob job;
    private readonly Task readTask;
    private readonly long subscriptionEpoch;
    private readonly long generationOrdinal;
    private Exception? failure;
    private bool disposed;

    private ObserverProcessSession(
        string executablePath,
        long subscriptionEpoch,
        long generationOrdinal)
    {
        this.subscriptionEpoch = subscriptionEpoch;
        this.generationOrdinal = generationOrdinal;
        string pipeName = $"QuickPods.Observer.{Guid.NewGuid():N}";
        pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        process = StartProcess(executablePath, pipeName);
        job = new OwnedProcessJob();
        try
        {
            job.Assign(process);
            using var timeout = new CancellationTokenSource(ConnectionTimeout);
            pipe.WaitForConnectionAsync(timeout.Token).GetAwaiter().GetResult();
            reader = new StreamReader(pipe, leaveOpen: true);
            writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            var request = new ObserverSessionRequest(
                QuickPodsProtocol.Version,
                subscriptionEpoch,
                generationOrdinal);
            writer.WriteLine(QuickPodsProtocolJson.Serialize(request));

            string readyMessage = reader.ReadLineAsync()
                .WaitAsync(ReadyTimeout)
                .GetAwaiter()
                .GetResult() ??
                throw new InvalidDataException("The taskbar observer exited before Ready.");
            ObserverInvalidationBatch ready =
                QuickPodsProtocolJson.DeserializeObserverInvalidation(readyMessage);
            ValidateIdentity(ready);
            if (!sequenceGate.TryAccept(ready.Sequence) ||
                ready.Kinds != ObserverInvalidationKind.Ready)
            {
                throw new InvalidDataException("The taskbar observer Ready envelope was invalid.");
            }

            readTask = ReadAsync(shutdown.Token);
        }
        catch
        {
            StopProcess();
            job.Dispose();
            pipe.Dispose();
            process.Dispose();
            shutdown.Dispose();
            throw;
        }
    }

    internal bool IsDisconnected => readTask.IsCompleted;

    internal Exception? Failure => Volatile.Read(ref failure);

    internal static ObserverProcessSession Start(
        string executablePath,
        long subscriptionEpoch,
        long generationOrdinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentOutOfRangeException.ThrowIfNegative(subscriptionEpoch);
        ArgumentOutOfRangeException.ThrowIfNegative(generationOrdinal);
        return new(executablePath, subscriptionEpoch, generationOrdinal);
    }

    internal bool TryDequeue(out ObserverInvalidationBatch? batch) =>
        pending.TryDequeue(out batch);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        shutdown.Cancel();
        pipe.Dispose();
        try
        {
            readTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }

        try
        {
            writer.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Closing the pipe is what unblocks the pending read. A final writer
            // flush cannot add value once that transport has been retired.
        }

        try
        {
            reader.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        StopProcess();
        process.Dispose();
        job.Dispose();
        shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
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

                ObserverInvalidationBatch batch =
                    QuickPodsProtocolJson.DeserializeObserverInvalidation(message);
                ValidateIdentity(batch);
                if (sequenceGate.TryAccept(batch.Sequence))
                {
                    pending.Enqueue(batch);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            Volatile.Write(ref failure, exception);
        }
    }

    private void ValidateIdentity(ObserverInvalidationBatch batch)
    {
        if (batch.SubscriptionEpoch != subscriptionEpoch ||
            batch.GenerationOrdinal != generationOrdinal)
        {
            throw new InvalidDataException("The taskbar observer epoch or generation is stale.");
        }
    }

    private static Process StartProcess(string executablePath, string pipeName)
    {
        string fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The QuickPods observer executable was not found.", fullPath);
        }

        var startInfo = new ProcessStartInfo(fullPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--pipe-name");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        return Process.Start(startInfo) ??
            throw new InvalidOperationException("The QuickPods observer process did not start.");
    }

    private void StopProcess()
    {
        if (process.HasExited)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(ExitTimeout);
        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }
}
