namespace QuickPods.TaskbarHost.Presentation;

internal static class TaskbarContinuityPolicy
{
    internal static bool CanRetainNativeSurface(
        TaskbarPresentationRoute nextRoute,
        bool placementIdentityMatches,
        bool continuityStable)
    {
        ArgumentNullException.ThrowIfNull(nextRoute);
        if (!continuityStable)
        {
            return false;
        }

        return (nextRoute.Surface == TaskbarPresentationSurface.Native &&
                placementIdentityMatches) ||
            (nextRoute.Surface == TaskbarPresentationSurface.Hidden &&
                nextRoute.Reason == TaskbarPresentationReason.TransientUnknownHidden);
    }

    internal static bool ShouldEnsureObserverAfterDiscovery(
        TaskbarPresentationRoute nextRoute)
    {
        ArgumentNullException.ThrowIfNull(nextRoute);
        return nextRoute.Surface == TaskbarPresentationSurface.Native;
    }
}
