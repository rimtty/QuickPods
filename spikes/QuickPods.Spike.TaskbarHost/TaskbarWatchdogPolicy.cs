using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;

namespace QuickPods.Spike.TaskbarHost;

internal readonly record struct TaskbarHostIdentity(
    nint TaskbarHandle,
    uint ExplorerProcessId,
    uint Dpi,
    PixelRect TaskbarBounds);

internal enum TaskbarWatchdogDecision
{
    KeepVisible,
    EscalateHiddenRecovery,
}

internal readonly record struct TaskbarWatchdogInput(
    TaskbarHostIdentity CurrentIdentity,
    PixelRect CurrentHostBounds,
    TaskbarHostIdentity? DiscoveredIdentity,
    PixelRect? DiscoveredHostBounds,
    bool InvalidatedDuringScan,
    bool CurrentBoundsRemainSafe,
    bool NativeAttachmentIsValid);

/// <summary>
/// Decides whether a notification-free periodic observation can leave the
/// already verified host visible. Any uncertainty escalates to the existing
/// hide-first recovery path, which performs another fresh scan while hidden.
/// </summary>
internal static class TaskbarWatchdogPolicy
{
    internal static TaskbarWatchdogDecision Decide(TaskbarWatchdogInput input)
    {
        if (input.InvalidatedDuringScan ||
            !input.NativeAttachmentIsValid ||
            !input.CurrentHostBounds.IsValid ||
            input.DiscoveredIdentity is not TaskbarHostIdentity discoveredIdentity ||
            input.DiscoveredHostBounds is not PixelRect discoveredBounds ||
            !discoveredBounds.IsValid ||
            input.CurrentIdentity != discoveredIdentity ||
            !input.CurrentBoundsRemainSafe)
        {
            return TaskbarWatchdogDecision.EscalateHiddenRecovery;
        }

        return TaskbarWatchdogDecision.KeepVisible;
    }
}
