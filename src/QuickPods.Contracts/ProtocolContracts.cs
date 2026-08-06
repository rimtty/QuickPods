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

[Flags]
public enum ObserverInvalidationKind
{
    None = 0,
    Ready = 1 << 0,
    StructureChanged = 1 << 1,
    BoundingRectangleChanged = 1 << 2,
    IsOffscreenChanged = 1 << 3,
    ExplorerGenerationChanged = 1 << 4,
    ObserverFaulted = 1 << 5,
}

public enum ObserverSourceClassification
{
    Unknown,
    Owned,
    External,
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

public sealed record ObserverSessionRequest
{
    public ObserverSessionRequest(
        int protocolVersion,
        long subscriptionEpoch,
        long generationOrdinal)
    {
        if (protocolVersion != QuickPodsProtocol.Version)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                protocolVersion,
                $"Protocol version {QuickPodsProtocol.Version} is required.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(subscriptionEpoch);
        ArgumentOutOfRangeException.ThrowIfNegative(generationOrdinal);
        ProtocolVersion = protocolVersion;
        SubscriptionEpoch = subscriptionEpoch;
        GenerationOrdinal = generationOrdinal;
    }

    public int ProtocolVersion { get; }

    public long SubscriptionEpoch { get; }

    public long GenerationOrdinal { get; }
}

public sealed record ObserverInvalidationBatch
{
    public ObserverInvalidationBatch(
        int protocolVersion,
        long sequence,
        long subscriptionEpoch,
        long generationOrdinal,
        ObserverInvalidationKind kinds,
        ObserverSourceClassification source)
    {
        if (protocolVersion != QuickPodsProtocol.Version)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                protocolVersion,
                $"Protocol version {QuickPodsProtocol.Version} is required.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentOutOfRangeException.ThrowIfNegative(subscriptionEpoch);
        ArgumentOutOfRangeException.ThrowIfNegative(generationOrdinal);
        if (kinds == ObserverInvalidationKind.None ||
            (kinds & ~AllObserverInvalidationKinds) != 0 ||
            ((kinds & ObserverInvalidationKind.Ready) != 0 &&
                kinds != ObserverInvalidationKind.Ready))
        {
            throw new ArgumentOutOfRangeException(nameof(kinds));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        ProtocolVersion = protocolVersion;
        Sequence = sequence;
        SubscriptionEpoch = subscriptionEpoch;
        GenerationOrdinal = generationOrdinal;
        Kinds = kinds;
        Source = source;
    }

    private const ObserverInvalidationKind AllObserverInvalidationKinds =
        ObserverInvalidationKind.Ready |
        ObserverInvalidationKind.StructureChanged |
        ObserverInvalidationKind.BoundingRectangleChanged |
        ObserverInvalidationKind.IsOffscreenChanged |
        ObserverInvalidationKind.ExplorerGenerationChanged |
        ObserverInvalidationKind.ObserverFaulted;

    public int ProtocolVersion { get; }

    public long Sequence { get; }

    public long SubscriptionEpoch { get; }

    public long GenerationOrdinal { get; }

    public ObserverInvalidationKind Kinds { get; }

    public ObserverSourceClassification Source { get; }
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

    public static string Serialize(ObserverSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SerializeBounded(request);
    }

    public static string Serialize(ObserverInvalidationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return SerializeBounded(batch);
    }

    public static HostStateEnvelope DeserializeState(string message) =>
        DeserializeBounded<HostStateEnvelope>(message);

    public static HostInteractionEnvelope DeserializeInteraction(string message) =>
        DeserializeBounded<HostInteractionEnvelope>(message);

    public static ObserverSessionRequest DeserializeObserverSession(string message) =>
        DeserializeBounded<ObserverSessionRequest>(message);

    public static ObserverInvalidationBatch DeserializeObserverInvalidation(string message) =>
        DeserializeBounded<ObserverInvalidationBatch>(message);

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
