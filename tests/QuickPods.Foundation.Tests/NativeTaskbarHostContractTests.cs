using QuickPods.TaskbarHost.Hosting;
using Xunit;

namespace QuickPods.Foundation.Tests;

public sealed class NativeTaskbarHostContractTests
{
    [Fact]
    public void PopupPreservedStyleForbidsChildAndTopmostBits()
    {
        Assert.True(NativeWindowStyles.MatchesPopupPreserved(NativeWindowStyles.PopupPreservedStyle));
        Assert.True(NativeWindowStyles.MatchesPopupPreserved(
            NativeWindowStyles.PopupPreservedStyle | NativeWindowStyles.WindowStyleVisible));
        Assert.False(NativeWindowStyles.MatchesPopupPreserved(
            NativeWindowStyles.PopupPreservedStyle | NativeWindowStyles.WindowStyleChild));

        Assert.True(NativeWindowStyles.MatchesRequiredExtendedStyle(
            NativeWindowStyles.RequiredExtendedStyle));
        Assert.False(NativeWindowStyles.MatchesRequiredExtendedStyle(
            NativeWindowStyles.RequiredExtendedStyle | NativeWindowStyles.WindowExtendedStyleTopmost));
    }

    [Fact]
    public void InteractionCalculatorClampsPointerAndWheelVolume()
    {
        Assert.True(SliderGeometry.TryCreate(300, 40, out SliderLayout layout));
        Assert.Equal(0, HostInteractionCalculator.VolumePercentFromPointer(layout, layout.TrackLeft - 20));
        Assert.Equal(100, HostInteractionCalculator.VolumePercentFromPointer(layout, layout.TrackRight + 20));
        Assert.Equal(2, HostInteractionCalculator.VolumePercentFromWheel(0, 120));
        Assert.Equal(100, HostInteractionCalculator.VolumePercentFromWheel(100, 120));
        Assert.Equal(0, HostInteractionCalculator.VolumePercentFromWheel(0, -120));
    }
}
