namespace QuickPods.Presentation;

public enum SystemSessionTransition
{
    Other,
    BecameUnavailable,
    BecameAvailable,
}

public sealed record SystemSessionRecoveryDecision(
    bool HideFlyout,
    bool ClearPlacementAnchor,
    bool SuspendRecovery,
    bool QueueRecovery);

public static class SystemSessionRecoveryPolicy
{
    public static SystemSessionRecoveryDecision Decide(SystemSessionTransition transition) =>
        transition switch
        {
            SystemSessionTransition.BecameUnavailable => new(
                HideFlyout: true,
                ClearPlacementAnchor: true,
                SuspendRecovery: true,
                QueueRecovery: false),
            SystemSessionTransition.BecameAvailable => new(
                HideFlyout: true,
                ClearPlacementAnchor: true,
                SuspendRecovery: false,
                QueueRecovery: true),
            _ => new(
                HideFlyout: false,
                ClearPlacementAnchor: false,
                SuspendRecovery: false,
                QueueRecovery: true),
        };
}
