namespace QuickPods.Core.Ports;

public interface IStartupRegistration
{
    ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    ValueTask SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default);
}
