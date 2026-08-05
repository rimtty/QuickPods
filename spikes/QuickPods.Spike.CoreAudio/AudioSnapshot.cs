namespace QuickPods.Spike.CoreAudio;

public sealed record AudioSnapshot(
    long Generation,
    string EndpointIdHash,
    string Role,
    uint DeviceState,
    float VolumeScalar,
    bool IsMuted,
    bool IsAvailable)
{
    public double VolumePercent => VolumeMath.ScalarToPercent(VolumeScalar);

    public static AudioSnapshot Unavailable(long generation, string role)
    {
        return new AudioSnapshot(
            generation,
            "none",
            role,
            DeviceState: 0,
            VolumeScalar: 0f,
            IsMuted: false,
            IsAvailable: false);
    }
}

public sealed record DefaultEndpointChange(
    long Generation,
    string EndpointIdHash,
    string Role,
    long Timestamp);

public readonly record struct NativeCallTiming(TimeSpan Duration);
