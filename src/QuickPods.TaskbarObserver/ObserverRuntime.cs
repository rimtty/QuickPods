using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
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
    private readonly TaskbarSignalSubscription subscription;
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
        subscription = TaskbarSignalSubscription.Start(WorkerTimeout);
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
            TimeoutException or
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
        while (!parentProcess.HasExited)
        {
            if (subscription.TryTake(out ObserverSignal first, TimeSpan.FromMilliseconds(250)))
            {
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

            if (subscription.Failure is { } failure)
            {
                _ = failure;
                WriteBatch(
                    ObserverInvalidationKind.ObserverFaulted,
                    ObserverSourceClassification.Unknown);
                return 6;
            }

            // The worker can enqueue a terminal generation signal and exit before
            // this loop is scheduled again. Only classify a clean worker exit after
            // draining that queue and checking its captured failure.
            if (!subscription.IsAlive)
            {
                return 6;
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
}
