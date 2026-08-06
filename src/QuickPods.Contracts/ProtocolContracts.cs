using System.Text.Json;

namespace QuickPods.Contracts;

public static class QuickPodsProtocol
{
    public const int Version = 1;

    public const int MaximumMessageCharacters = 16 * 1024;
}

public enum TaskbarSurfaceMode
{
    Native,
    Floating,
    Hidden,
}

public enum HostInteractionKind
{
    SetVolumePreview,
    SetVolumeCommit,
    ToggleMute,
    OpenAudioFlyout,
    OpenContextMenu,
}

public sealed record TaskbarDeviceView(
    string DeviceKey,
    string DisplayName,
    string StatusText);

public sealed record TaskbarStateSnapshot(
    TaskbarSurfaceMode SurfaceMode,
    int VolumePercent,
    bool IsMuted,
    TaskbarDeviceView? SelectedDevice);

public sealed record HostStateEnvelope
{
    public HostStateEnvelope(int protocolVersion, long sequence, TaskbarStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (protocolVersion != QuickPodsProtocol.Version)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                protocolVersion,
                $"Protocol version {QuickPodsProtocol.Version} is required.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sequence);

        ProtocolVersion = protocolVersion;
        Sequence = sequence;
        Snapshot = snapshot;
    }

    public int ProtocolVersion { get; }

    public long Sequence { get; }

    public TaskbarStateSnapshot Snapshot { get; }
}

public sealed record HostInteractionEnvelope
{
    public HostInteractionEnvelope(
        int protocolVersion,
        long sequence,
        HostInteractionKind kind,
        int? volumePercent = null)
    {
        if (protocolVersion != QuickPodsProtocol.Version)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                protocolVersion,
                $"Protocol version {QuickPodsProtocol.Version} is required.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sequence);

        bool volumeInteraction = kind is HostInteractionKind.SetVolumePreview or HostInteractionKind.SetVolumeCommit;
        if (volumeInteraction != volumePercent.HasValue)
        {
            throw new ArgumentException("Volume interactions require a value and other interactions forbid it.", nameof(volumePercent));
        }

        if (volumePercent is { } value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(volumePercent));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100, nameof(volumePercent));
        }

        ProtocolVersion = protocolVersion;
        Sequence = sequence;
        Kind = kind;
        VolumePercent = volumePercent;
    }

    public int ProtocolVersion { get; }

    public long Sequence { get; }

    public HostInteractionKind Kind { get; }

    public int? VolumePercent { get; }
}

public sealed class MonotonicSequenceGate
{
    private long lastAccepted = -1;

    public long LastAccepted => Interlocked.Read(ref lastAccepted);

    public bool TryAccept(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);

        while (true)
        {
            long observed = Interlocked.Read(ref lastAccepted);
            if (sequence <= observed)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref lastAccepted, sequence, observed) == observed)
            {
                return true;
            }
        }
    }

    public void Reset() => Interlocked.Exchange(ref lastAccepted, -1);
}

public static class QuickPodsProtocolJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(HostStateEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return SerializeBounded(envelope);
    }

    public static string Serialize(HostInteractionEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return SerializeBounded(envelope);
    }

    public static HostStateEnvelope DeserializeState(string message) =>
        DeserializeBounded<HostStateEnvelope>(message);

    public static HostInteractionEnvelope DeserializeInteraction(string message) =>
        DeserializeBounded<HostInteractionEnvelope>(message);

    private static string SerializeBounded<T>(T value)
    {
        string message = JsonSerializer.Serialize(value, SerializerOptions);
        if (message.Length > QuickPodsProtocol.MaximumMessageCharacters)
        {
            throw new InvalidDataException("The QuickPods IPC message exceeds the protocol limit.");
        }

        return message;
    }

    private static T DeserializeBounded<T>(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Length > QuickPodsProtocol.MaximumMessageCharacters)
        {
            throw new InvalidDataException("The QuickPods IPC message exceeds the protocol limit.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(message, SerializerOptions) ??
                throw new InvalidDataException("The QuickPods IPC message did not contain an envelope.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The QuickPods IPC message is malformed.", exception);
        }
    }
}
