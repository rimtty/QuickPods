namespace QuickPods.TaskbarHost.Hosting;

internal static class NativeWindowStyles
{
    internal const uint WindowStyleVisible = 0x10000000;
    internal const uint WindowStyleChild = 0x40000000;
    internal const uint WindowStylePopup = 0x80000000;
    internal const uint WindowStyleClipSiblings = 0x04000000;

    internal const uint WindowExtendedStyleTopmost = 0x00000008;
    internal const uint WindowExtendedStyleToolWindow = 0x00000080;
    internal const uint WindowExtendedStyleLayered = 0x00080000;
    internal const uint WindowExtendedStyleNoActivate = 0x08000000;

    internal const uint PopupPreservedStyle = WindowStylePopup | WindowStyleClipSiblings;
    internal const uint RequiredExtendedStyle =
        WindowExtendedStyleToolWindow |
        WindowExtendedStyleLayered |
        WindowExtendedStyleNoActivate;

    internal static bool MatchesPopupPreserved(uint style) =>
        (style & ~WindowStyleVisible) == PopupPreservedStyle &&
        (style & WindowStyleChild) == 0;

    internal static bool MatchesRequiredExtendedStyle(uint extendedStyle) =>
        extendedStyle == RequiredExtendedStyle &&
        (extendedStyle & WindowExtendedStyleTopmost) == 0;
}
