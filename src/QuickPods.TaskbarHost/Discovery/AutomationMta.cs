using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace QuickPods.TaskbarHost.Discovery;

internal static class AutomationMta
{
    private static readonly BlockingCollection<IWorkItem> Queue = new();
    // A completed Thread retains three native handles until finalization. Reusing one
    // worker keeps the five-second watchdog scan independent of GC timing.
    private static readonly Thread Worker = StartWorker();
    private static int isBusy;

    public static async Task<T> RunAsync<T>(
        Func<T> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        _ = Worker;

        if (Interlocked.CompareExchange(ref isBusy, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A previous UI Automation operation is still running.");
        }

        var work = new WorkItem<T>(operation);
        try
        {
            Queue.Add(work, CancellationToken.None);
        }
        catch
        {
            Interlocked.Exchange(ref isBusy, 0);
            throw;
        }

        return await work.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    private static Thread StartWorker()
    {
        var thread = new Thread(ExecuteLoop)
        {
            IsBackground = true,
            Name = "QuickPods UI Automation MTA",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return thread;
    }

    private static void ExecuteLoop()
    {
        foreach (IWorkItem work in Queue.GetConsumingEnumerable())
        {
            work.Execute();
        }
    }

    private interface IWorkItem
    {
        void Execute();
    }

    private sealed class WorkItem<T>(Func<T> operation) : IWorkItem
    {
        private readonly TaskCompletionSource<T> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task<T> Task => completion.Task;

        [SuppressMessage(
            "Design",
            "CA1031:Do not catch general exception types",
            Justification = "Exceptions must be returned to the caller and never escape the native worker thread.")]
        public void Execute()
        {
            T result = default!;
            Exception? failure = null;
            try
            {
                if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
                {
                    throw new InvalidOperationException(
                        "The UI Automation worker was not initialized as MTA.");
                }

                result = operation();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                Interlocked.Exchange(ref isBusy, 0);
            }

            if (failure is null)
            {
                completion.TrySetResult(result);
            }
            else
            {
                completion.TrySetException(failure);
            }
        }
    }
}
