using QuickPods.Presentation;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class FlyoutDismissPolicyTests
{
    [Fact]
    public void PreviewPollingRearmsOverFlyoutAndHidesAfterPointerLeaves()
    {
        Assert.Equal(
            FlyoutDismissAction.Ignore,
            FlyoutDismissPolicy.Decide(previewActive: false, pointerWithinFlyout: false));
        Assert.Equal(
            FlyoutDismissAction.Rearm,
            FlyoutDismissPolicy.Decide(previewActive: true, pointerWithinFlyout: true));
        Assert.Equal(
            FlyoutDismissAction.Hide,
            FlyoutDismissPolicy.Decide(previewActive: true, pointerWithinFlyout: false));
    }
}
