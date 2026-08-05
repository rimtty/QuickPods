namespace QuickPods.Spike.CoreAudio;

internal interface IAudioStateAccess
{
    void SetMuted(bool isMuted);

    void SetVolumeScalar(float scalar);

    bool GetMuted();

    float GetVolumeScalar();
}

internal static class AudioStateRestorer
{
    public static bool RestoreAndVerify(
        IAudioStateAccess endpoint,
        float originalVolumeScalar,
        bool originalMuted)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        endpoint.SetMuted(isMuted: true);
        endpoint.SetVolumeScalar(originalVolumeScalar);
        endpoint.SetMuted(originalMuted);

        float observedVolume = endpoint.GetVolumeScalar();
        bool observedMuted = endpoint.GetMuted();
        return Math.Abs(observedVolume - originalVolumeScalar) <= 0.01f &&
            observedMuted == originalMuted;
    }
}
