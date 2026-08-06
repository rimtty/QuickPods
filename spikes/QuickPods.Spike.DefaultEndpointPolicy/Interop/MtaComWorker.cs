using System.Collections.Concurrent;

namespace QuickPods.Spike.DefaultEndpointPolicy.Interop;

internal sealed class MtaComWorker : IDisposable
{
    private readonly BlockingCollection<Action> _work = new(boundedCapacity: 64);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private int _disposed;
    private int _workerThreadId;

    internal MtaComWorker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "QuickPods Default Endpoint MTA",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _started.Task.GetAwaiter().GetResult();
    }

    internal T Invoke<T>(Func<T> function)
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

    internal void Invoke(Action action) => Invoke(() =>
    {
        action();
        return true;
    });

    internal bool TryPost(Action action)
    {
        if (Volatile.Read(ref _disposed) != 0 || _work.IsAddingCompleted)
        {
            return false;
        }

        try
        {
            return _work.TryAdd(action);
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

        _work.CompleteAdding();
        if (Environment.CurrentManagedThreadId != _workerThreadId &&
            !_thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException("The default-endpoint COM worker did not stop.");
        }

        _work.Dispose();
    }

    private void Run()
    {
        int result = NativeMethods.CoInitializeEx(nint.Zero, NativeAudioConstants.CoInitMultithreaded);
        if (result < 0)
        {
            _started.TrySetException(new WindowsAudioInteropException(
                nameof(NativeMethods.CoInitializeEx),
                result));
            return;
        }

        _workerThreadId = Environment.CurrentManagedThreadId;
        _started.TrySetResult();
        try
        {
            foreach (Action action in _work.GetConsumingEnumerable())
            {
                action();
            }
        }
        finally
        {
            NativeMethods.CoUninitialize();
        }
    }
}
