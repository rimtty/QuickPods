using System.Text.Json;

namespace QuickPods.Infrastructure.Logging;

public enum QuickPodsLogLevel
{
    Debug,
    Information,
    Warning,
    Error,
}

public sealed record QuickPodsLogEntry(
    DateTimeOffset Timestamp,
    QuickPodsLogLevel Level,
    string EventName,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);

public interface IQuickPodsLogger
{
    ValueTask WriteAsync(QuickPodsLogEntry entry, CancellationToken cancellationToken = default);
}

public sealed class JsonLineLogger : IQuickPodsLogger, IAsyncDisposable
{
    private readonly TextWriter writer;
    private readonly SemaphoreSlim gate = new(1, 1);

    public JsonLineLogger(TextWriter writer)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async ValueTask WriteAsync(
        QuickPodsLogEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string json = JsonSerializer.Serialize(entry);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await writer.DisposeAsync().ConfigureAwait(false);
        gate.Dispose();
    }
}
