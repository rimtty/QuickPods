namespace QuickPods.Spike.DefaultEndpointPolicy;

internal interface IDefaultAudioEndpointPolicy
{
    ValueTask<OpaqueEndpointHandle?> GetDefaultEndpointAsync(
        DefaultEndpointRole role,
        CancellationToken cancellationToken);

    ValueTask<DefaultEndpointWriteResult> SetDefaultEndpointAsync(
        OpaqueEndpointHandle endpoint,
        DefaultEndpointRole role,
        CancellationToken cancellationToken);

    ValueTask<DefaultEndpointNotification> WaitForDefaultEndpointChangedAsync(
        OpaqueEndpointHandle endpoint,
        DefaultEndpointRole role,
        long generation,
        CancellationToken cancellationToken);
}

internal interface IDefaultEndpointGenerationFence
{
    bool IsCurrent(long generation);
}
