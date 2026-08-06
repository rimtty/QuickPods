using System.Collections.Concurrent;

namespace QuickPods.Spike.BluetoothKs.Operations;

public sealed class ContainerOperationSerializer
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _lanes =
        new(StringComparer.Ordinal);

    public async Task<ContainerOperationLease> AcquireAsync(
        string containerKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerKey);
        SemaphoreSlim lane = _lanes.GetOrAdd(
            containerKey,
            static _ => new SemaphoreSlim(1, 1));
        await lane.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new ContainerOperationLease(lane);
    }
}

public sealed class ContainerOperationLease : IAsyncDisposable
{
    private readonly SemaphoreSlim _lane;
    private Task? _releaseBarrier;
    private int _disposed;

    internal ContainerOperationLease(SemaphoreSlim lane)
    {
        _lane = lane;
    }

    public void HoldUntil(Task completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _releaseBarrier = _releaseBarrier is null
            ? completion
            : Task.WhenAll(_releaseBarrier, completion);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        if (_releaseBarrier is null)
        {
            _lane.Release();
        }
        else
        {
            _ = ReleaseAfterBarrierAsync(_releaseBarrier, _lane);
        }

        return ValueTask.CompletedTask;
    }

    private static async Task ReleaseAfterBarrierAsync(Task barrier, SemaphoreSlim lane)
    {
        try
        {
            await barrier.ConfigureAwait(false);
        }
        catch
        {
            // Completion faults are reported by the operation; the lane still must be released.
        }
        finally
        {
            lane.Release();
        }
    }
}
