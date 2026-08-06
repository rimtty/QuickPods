using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace QuickPods.Windows.Audio.Interop;

internal sealed partial class MtaAudioWorker : IDisposable
{
    private const uint CoinitMultithreaded = 0;

    private readonly BlockingCollection<Action> workItems = new(boundedCapacity: 128);
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private int disposed;
    private int workerThreadId;

    public MtaAudioWorker()
    {
        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "QuickPods Core Audio MTA",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        started.Task.GetAwaiter().GetResult();
    }

    public event Action<Exception>? BackgroundFaulted;

    public T Invoke<T>(Func<T> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        if (Environment.CurrentManagedThreadId == workerThreadId)
        {
            return function();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool posted = TryPost(() =>
        {
            try
            {
                completion.TrySetResult(function());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        ObjectDisposedException.ThrowIf(!posted, this);
        return completion.Task.GetAwaiter().GetResult();
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = Invoke(() =>
        {
            action();
            return true;
        });
    }

    public async ValueTask<T> InvokeAsync<T>(
        Func<T> function,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(function);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool posted = TryPost(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.TrySetResult(function());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        ObjectDisposedException.ThrowIf(!posted, this);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryPost(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Volatile.Read(ref disposed) != 0 || workItems.IsAddingCompleted)
        {
            return false;
        }

        try
        {
            return workItems.TryAdd(action);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        workItems.CompleteAdding();
        if (Environment.CurrentManagedThreadId != workerThreadId && !thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("The Core Audio MTA worker did not stop within five seconds.");
        }

        workItems.Dispose();
    }

    private void Run()
    {
        int result = NativeMethods.CoInitializeEx(nint.Zero, CoinitMultithreaded);
        if (result < 0)
        {
            started.TrySetException(new CoreAudioInteropException(nameof(NativeMethods.CoInitializeEx), result));
            return;
        }

        workerThreadId = Environment.CurrentManagedThreadId;
        started.TrySetResult();
        try
        {
            foreach (Action workItem in workItems.GetConsumingEnumerable())
            {
                try
                {
                    workItem();
                }
                catch (Exception exception)
                {
                    try
                    {
                        BackgroundFaulted?.Invoke(exception);
                    }
                    catch
                    {
                    }
                }
            }
        }
        finally
        {
            NativeMethods.CoUninitialize();
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("ole32.dll")]
        internal static partial int CoInitializeEx(nint reserved, uint initializationType);

        [LibraryImport("ole32.dll")]
        internal static partial void CoUninitialize();
    }
}
