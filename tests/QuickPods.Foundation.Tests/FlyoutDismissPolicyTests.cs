using QuickPods.Presentation;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class FlyoutDismissPolicyTests
{
    [Fact]
    public void AutoDismissPollingRearmsOverFlyoutAndHidesAfterPointerLeaves()
    {
        Assert.Equal(
            FlyoutDismissAction.Ignore,
            FlyoutDismissPolicy.Decide(autoDismissActive: false, pointerWithinFlyout: false));
        Assert.Equal(
            FlyoutDismissAction.Rearm,
            FlyoutDismissPolicy.Decide(autoDismissActive: true, pointerWithinFlyout: true));
        Assert.Equal(
            FlyoutDismissAction.Hide,
            FlyoutDismissPolicy.Decide(autoDismissActive: true, pointerWithinFlyout: false));
    }
}
