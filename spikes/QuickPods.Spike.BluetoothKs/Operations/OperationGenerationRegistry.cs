using System.Collections.Concurrent;

namespace QuickPods.Spike.BluetoothKs.Operations;

public sealed class OperationGenerationRegistry
{
    private readonly ConcurrentDictionary<string, GenerationCounter> _generations =
        new(StringComparer.Ordinal);

    public long Begin(string containerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerKey);
        GenerationCounter counter = _generations.GetOrAdd(
            containerKey,
            static _ => new GenerationCounter());
        return counter.Advance();
    }

    public bool IsCurrent(string containerKey, long generation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerKey);
        return _generations.TryGetValue(containerKey, out GenerationCounter? counter) &&
            counter.Current == generation;
    }

    public bool TryAccept(BluetoothOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return IsCurrent(result.Request.ContainerKey, result.Request.Generation);
    }

    private sealed class GenerationCounter
    {
        private long _current;

        public long Current => Interlocked.Read(ref _current);

        public long Advance()
        {
            return Interlocked.Increment(ref _current);
        }
    }
}
