namespace QuickPods.Spike.CoreAudio;

public sealed record VolumeNotification(
    long Generation,
    Guid EventContext,
    bool IsMuted,
    float VolumeScalar,
    long CallbackTimestamp)
{
    public double VolumePercent => VolumeMath.ScalarToPercent(VolumeScalar);

    public bool IsSelfOriginated(Guid applicationEventContext)
    {
        return EventContext == applicationEventContext;
    }
}

public sealed class GenerationGate
{
    private long _current;

    public long Current => Interlocked.Read(ref _current);

    public long Advance()
    {
        return Interlocked.Increment(ref _current);
    }

    public bool IsCurrent(long generation)
    {
        return generation == Current;
    }
}
