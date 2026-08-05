namespace QuickPods.Spike.DefaultEndpointPolicy;

internal static class DefaultEndpointTargetResolver
{
    internal static DefaultEndpointTargetResolution Resolve(
        IEnumerable<DefaultEndpointCandidate> candidates,
        OpaqueContainerHandle selectedContainer)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(selectedContainer);

        DefaultEndpointCandidate[] eligible = [.. candidates.Where(candidate =>
            candidate.Container.Equals(selectedContainer) &&
            candidate.Flow == AudioDataFlow.Render &&
            candidate.Profile == BluetoothAudioProfile.Stereo &&
            candidate.Availability == AudioEndpointAvailability.Active)];
        return eligible.Length switch
        {
            1 => new DefaultEndpointTargetResolution(
                DefaultEndpointTargetStatus.Selected,
                eligible[0]),
            0 => new DefaultEndpointTargetResolution(
                DefaultEndpointTargetStatus.NoActiveStereoEndpoint,
                null),
            _ => new DefaultEndpointTargetResolution(
                DefaultEndpointTargetStatus.AmbiguousActiveStereoEndpoints,
                null),
        };
    }
}
