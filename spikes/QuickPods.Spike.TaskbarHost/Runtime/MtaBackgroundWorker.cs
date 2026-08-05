using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;

namespace QuickPods.Spike.TaskbarHost.Runtime;

/// <summary>
/// Runs one synchronous COM/UIA work item on an explicitly initialized MTA
/// background thread. A timeout abandons only the result; the background thread
/// is never aborted because Thread.Abort is unsafe and unsupported.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MtaBackgroundWorker
{
    private static int _isBusy;

    internal static Task<T> RunAsync<T>(
        Func<T> callback,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _isBusy, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A previous UI Automation operation is still running.");
        }

        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread worker = new(
            () => Execute(callback, completion))
        {
            IsBackground = true,
            Name = "QuickPods.TaskbarDiscovery.MTA",
        };
        try
        {
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }
        catch
        {
            Interlocked.Exchange(ref _isBusy, 0);
            throw;
        }

        return AwaitAsync(completion.Task, timeout, cancellationToken);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An exception must never escape the native background-thread entry point and terminate the process.")]
    private static void Execute<T>(Func<T> callback, TaskCompletionSource<T> completion)
    {
        try
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
            {
                throw new InvalidOperationException("The UI Automation worker was not initialized as MTA.");
            }

            completion.TrySetResult(callback());
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            Interlocked.Exchange(ref _isBusy, 0);
        }
    }

    private static async Task<T> AwaitAsync<T>(
        Task<T> work,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return await work.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
    }
}
