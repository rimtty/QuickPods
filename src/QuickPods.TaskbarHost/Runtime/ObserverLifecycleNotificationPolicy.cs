using QuickPods.Contracts;

namespace QuickPods.TaskbarHost.Runtime;

internal static class ObserverLifecycleNotificationPolicy
{
    internal static HostInteractionKind ClassifyRetirement(
        ObserverInvalidationKind terminalKinds,
        bool hasTransportFailure)
    {
        if (hasTransportFailure ||
            (terminalKinds & ObserverInvalidationKind.ObserverFaulted) != 0)
        {
            return HostInteractionKind.TaskbarObserverFaulted;
        }

        return (terminalKinds & ObserverInvalidationKind.ExplorerGenerationChanged) != 0
            ? HostInteractionKind.TaskbarObserverGenerationChanged
            : HostInteractionKind.TaskbarObserverDisconnected;
    }
}
