using QuickPods.Core.Models;

namespace QuickPods.Core.Ports;

public interface IApplicationUpdateChecker
{
    ValueTask<ApplicationUpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default);
}
