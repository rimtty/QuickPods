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

    ValueTask<IDefaultEndpointNotificationSubscription> SubscribeDefaultEndpointChangedAsync(
        OpaqueEndpointHandle endpoint,
        DefaultEndpointRole role,
        long generation,
        CancellationToken cancellationToken);
}

internal interface IDefaultEndpointNotificationSubscription : IAsyncDisposable
{
    ValueTask<DefaultEndpointNotification> WaitAsync(
        CancellationToken cancellationToken);
}

internal interface IDefaultEndpointGenerationFence
{
    bool IsCurrent(long generation);
}
