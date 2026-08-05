using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace QuickPods.Spike.BluetoothKs.Runtime;

internal sealed partial class MtaComWorker : IDisposable
{
    private const uint CoinitMultithreaded = 0x0;

    private readonly BlockingCollection<Action> _workItems = new(boundedCapacity: 64);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private int _disposed;
    private int _workerThreadId;

    public MtaComWorker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "QuickPods Bluetooth KS MTA",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();

        try
        {
            _started.Task.GetAwaiter().GetResult();
        }
        catch
        {
            Interlocked.Exchange(ref _disposed, 1);
            _workItems.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(1));
            _workItems.Dispose();
            throw;
        }
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
        if (Environment.CurrentManagedThreadId != _workerThreadId &&
            !_thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("The Bluetooth KS MTA worker did not stop within five seconds.");
        }

        _workItems.Dispose();
    }

    private void Run()
    {
        int initializeResult = NativeMethods.CoInitializeEx(nint.Zero, CoinitMultithreaded);
        if (initializeResult < 0)
        {
            _started.TrySetException(new ComInitializationException(initializeResult));
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

internal sealed class ComInitializationException : InvalidOperationException
{
    public ComInitializationException(int nativeHResult)
        : base($"CoInitializeEx failed with HRESULT 0x{nativeHResult:X8}.")
    {
        NativeHResult = nativeHResult;
    }

    public int NativeHResult { get; }
}
