namespace QuickPods.TaskbarHost.Discovery;

internal static class TaskbarDiscoveryService
{
    public static async Task<TaskbarDiscoveryResult> DiscoverAsync(
        nint ignoredHost = default,
        CancellationToken cancellationToken = default)
    {
        NativeTaskbarProbe native = Win32TaskbarDiscovery.Discover(ignoredHost);
        if (!native.HasTarget)
        {
            return new TaskbarDiscoveryResult(
                null,
                [.. native.Faults.Distinct()],
                native.IgnoredHostMatched);
        }

        AutomationTaskbarProbe automation = await TaskbarAutomationDiscovery.DiscoverAsync(
            native.TaskbarHandle,
            native.Bounds,
            cancellationToken).ConfigureAwait(false);
        TaskbarDiscoveryFault[] faults =
        [
            .. native.Faults.Concat(automation.Faults).Distinct(),
        ];
        var snapshot = new LiveTaskbarSnapshot(
            native.TaskbarHandle,
            native.ExplorerProcessId,
            native.Bounds,
            native.Dpi,
            native.MonitorBounds,
            native.WorkArea,
            native.Obstacles,
            automation.Buttons);
        return new TaskbarDiscoveryResult(snapshot, faults, native.IgnoredHostMatched);
    }
}
