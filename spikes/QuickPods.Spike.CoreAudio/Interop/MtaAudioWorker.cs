using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace QuickPods.Spike.CoreAudio.Interop;

internal sealed partial class MtaAudioWorker : IDisposable
{
    private const uint CoinitMultithreaded = 0x0;

    private readonly BlockingCollection<Action> _workItems = new(boundedCapacity: 64);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private int _disposed;
    private int _workerThreadId;

    public MtaAudioWorker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "QuickPods Core Audio MTA",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _started.Task.GetAwaiter().GetResult();
    }

    public event Action<Exception>? BackgroundFaulted;

    public T Invoke<T>(Func<T> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Environment.CurrentManagedThreadId == _workerThreadId)
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
        Invoke(() =>
        {
            action();
            return true;
        });
    }

    public bool TryPost(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Volatile.Read(ref _disposed) != 0 || _workItems.IsAddingCompleted)
        {
            return false;
        }

        try
        {
            return _workItems.TryAdd(action);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _workItems.CompleteAdding();

        if (Environment.CurrentManagedThreadId != _workerThreadId && !_thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("The Core Audio MTA worker did not stop within five seconds.");
        }

        _workItems.Dispose();
    }

    private void Run()
    {
        int initializeResult = NativeMethods.CoInitializeEx(nint.Zero, CoinitMultithreaded);
        if (initializeResult < 0)
        {
            _started.TrySetException(new CoreAudioInteropException(
                nameof(NativeMethods.CoInitializeEx),
                initializeResult));
            return;
        }

        _workerThreadId = Environment.CurrentManagedThreadId;
        _started.TrySetResult();

        try
        {
            foreach (Action workItem in _workItems.GetConsumingEnumerable())
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
                        // A diagnostic subscriber must not terminate the COM worker.
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
