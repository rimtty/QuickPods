using QuickPods.Spike.TaskbarHost.Hosting;
using QuickPods.Spike.TaskbarHost.Interop;

namespace QuickPods.Spike.TaskbarHost.Tests;

public sealed class NativeHostStyleTests
{
    [Theory]
    [InlineData((int)NativeParentStyleMode.PopupPreserved)]
    [InlineData((int)NativeParentStyleMode.Child)]
    public void Styles_preserve_all_required_mode_and_extended_bits(int modeValue)
    {
        var mode = (NativeParentStyleMode)modeValue;
        long style = NativeWindowStyles.GetStyle(mode);

        Assert.True(NativeWindowStyles.MatchesParentStyleMode(style, mode));
        Assert.NotEqual(0, style & NativeConstants.WindowStyleClipSiblings);
        Assert.True(NativeWindowStyles.HasRequiredExtendedStyles(NativeWindowStyles.RequiredExtendedStyle));
        Assert.Equal(
            NativeConstants.WindowExtendedStyleToolWindow |
            NativeConstants.WindowExtendedStyleLayered |
            NativeConstants.WindowExtendedStyleNoActivate,
            NativeWindowStyles.RequiredExtendedStyle);
    }
}
