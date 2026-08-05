namespace QuickPods.Spike.CoreAudio;

public sealed class CoreAudioMutationScope : IDisposable
{
    private readonly CoreAudioClient _client;
    private readonly long _leaseId;
    private int _ended;

    internal CoreAudioMutationScope(
        CoreAudioClient client,
        long leaseId,
        AudioSnapshot originalSnapshot)
    {
        _client = client;
        _leaseId = leaseId;
        OriginalSnapshot = originalSnapshot;
    }

    public AudioSnapshot OriginalSnapshot { get; }

    public NativeCallTiming MuteForSafety()
    {
        ThrowIfEnded();
        return _client.MuteLeasedEndpoint(_leaseId);
    }

    public NativeCallTiming SetVolumePercent(double percent)
    {
        ThrowIfEnded();
        return _client.SetLeasedVolumePercent(_leaseId, percent);
    }

    public bool Restore()
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
        {
            throw new InvalidOperationException("The guarded Core Audio mutation has already ended.");
        }

        bool restored = _client.RestoreAndEndMutation(_leaseId);
        if (!restored)
        {
            throw new AudioRestorationException();
        }

        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0)
        {
            if (!_client.RestoreAndEndMutation(_leaseId))
            {
                throw new AudioRestorationException();
            }
        }
    }

    private void ThrowIfEnded()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _ended) != 0, this);
    }
}

internal sealed class AudioRestorationException : InvalidOperationException
{
    public AudioRestorationException()
        : base("The original endpoint volume and mute state could not be verified after restoration.")
    {
    }
}
