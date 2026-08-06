using System.Text.Json;

namespace QuickPods.Infrastructure.Settings;

public interface ISettingsStore<T>
{
    ValueTask<T?> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(T value, CancellationToken cancellationToken = default);
}

public sealed record SettingsRecoveryInfo(
    string OriginalPath,
    string QuarantinedPath,
    string FailureType);

public sealed class JsonSettingsStore<T> : ISettingsStore<T>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
    };

    private readonly string path;

    public event EventHandler<SettingsRecoveryInfo>? CorruptSettingsQuarantined;

    public SettingsRecoveryInfo? LastRecovery { get; private set; }

    public JsonSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    public async ValueTask<T?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            SettingsRecoveryInfo recovery = QuarantineCorruptSettings(exception);
            LastRecovery = recovery;
            try
            {
                CorruptSettingsQuarantined?.Invoke(this, recovery);
            }
            catch
            {
                // Recovery must not depend on diagnostics subscribers.
            }

            return default;
        }
    }

    public async ValueTask SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private SettingsRecoveryInfo QuarantineCorruptSettings(JsonException exception)
    {
        string directory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        string quarantinePath = Path.Combine(
            directory,
            $"{name}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}{extension}");
        File.Move(path, quarantinePath);
        return new SettingsRecoveryInfo(path, quarantinePath, exception.GetType().Name);
    }
}
