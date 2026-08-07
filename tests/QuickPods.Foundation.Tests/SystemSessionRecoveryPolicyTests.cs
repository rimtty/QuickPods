using QuickPods.Presentation;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class SystemSessionRecoveryPolicyTests
{
    [Fact]
    public void UnavailableSessionHidesStaleFlyoutWithoutRunningRecoveryBehindLockScreen()
    {
        SystemSessionRecoveryDecision decision = SystemSessionRecoveryPolicy.Decide(
            SystemSessionTransition.BecameUnavailable);

        Assert.True(decision.HideFlyout);
        Assert.True(decision.ClearPlacementAnchor);
        Assert.True(decision.SuspendRecovery);
        Assert.False(decision.QueueRecovery);
    }

    [Fact]
    public void AvailableSessionKeepsFlyoutHiddenAndQueuesFreshRecovery()
    {
        SystemSessionRecoveryDecision decision = SystemSessionRecoveryPolicy.Decide(
            SystemSessionTransition.BecameAvailable);

        Assert.True(decision.HideFlyout);
        Assert.True(decision.ClearPlacementAnchor);
        Assert.False(decision.SuspendRecovery);
        Assert.True(decision.QueueRecovery);
    }
}
