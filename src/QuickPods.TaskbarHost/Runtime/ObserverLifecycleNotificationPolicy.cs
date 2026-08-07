using QuickPods.Contracts;

namespace QuickPods.TaskbarHost.Runtime;

internal static class ObserverLifecycleNotificationPolicy
{
    internal static HostInteractionKind ClassifyRetirement(
        ObserverInvalidationKind terminalKinds,
        bool hasTransportFailure,
        bool explorerGenerationExited = false)
    {
        if (explorerGenerationExited ||
            (terminalKinds & ObserverInvalidationKind.ExplorerGenerationChanged) != 0)
        {
            return HostInteractionKind.TaskbarObserverGenerationChanged;
        }

        if (hasTransportFailure ||
            (terminalKinds & ObserverInvalidationKind.ObserverFaulted) != 0)
        {
            return HostInteractionKind.TaskbarObserverFaulted;
        }

        return HostInteractionKind.TaskbarObserverDisconnected;
    }

    internal static HostInteractionKind ClassifyReplacement(
        uint observerExplorerProcessId,
        uint discoveredExplorerProcessId,
        bool hasTransportFailure)
    {
        if (observerExplorerProcessId != 0 &&
            discoveredExplorerProcessId != 0 &&
            observerExplorerProcessId != discoveredExplorerProcessId)
        {
            return HostInteractionKind.TaskbarObserverGenerationChanged;
        }

        return hasTransportFailure
            ? HostInteractionKind.TaskbarObserverFaulted
            : HostInteractionKind.TaskbarObserverDisconnected;
    }
}
