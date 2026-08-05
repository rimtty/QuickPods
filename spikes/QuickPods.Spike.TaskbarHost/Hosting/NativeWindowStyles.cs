using QuickPods.Spike.TaskbarHost.Interop;

namespace QuickPods.Spike.TaskbarHost.Hosting;

internal static class NativeWindowStyles
{
    internal const long RequiredExtendedStyle =
        NativeConstants.WindowExtendedStyleToolWindow |
        NativeConstants.WindowExtendedStyleLayered |
        NativeConstants.WindowExtendedStyleNoActivate;

    internal static long GetInitialStyle() =>
        NativeConstants.WindowStylePopup |
        NativeConstants.WindowStyleClipSiblings;

    internal static long GetFloatingStyle() => GetInitialStyle();

    internal static long GetFloatingExtendedStyle() => RequiredExtendedStyle;

    internal static long GetStyle(NativeParentStyleMode mode) => mode switch
    {
        NativeParentStyleMode.PopupPreserved => GetInitialStyle(),
        NativeParentStyleMode.Child =>
            (GetInitialStyle() & ~NativeConstants.WindowStylePopup) |
            NativeConstants.WindowStyleChild,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown parent style mode."),
    };

    internal static bool HasRequiredExtendedStyles(long style) =>
        (style & RequiredExtendedStyle) == RequiredExtendedStyle;

    internal static bool MatchesParentStyleMode(long style, NativeParentStyleMode mode) => mode switch
    {
        NativeParentStyleMode.PopupPreserved =>
            (style & NativeConstants.WindowStylePopup) != 0 &&
            (style & NativeConstants.WindowStyleChild) == 0,
        NativeParentStyleMode.Child =>
            (style & NativeConstants.WindowStyleChild) != 0 &&
            (style & NativeConstants.WindowStylePopup) == 0,
        _ => false,
    };

    internal static bool MatchesFloatingWindow(long style, long extendedStyle) =>
        (style & NativeConstants.WindowStylePopup) != 0 &&
        (style & NativeConstants.WindowStyleChild) == 0 &&
        HasRequiredExtendedStyles(extendedStyle) &&
        (extendedStyle & NativeConstants.WindowExtendedStyleTopmost) == 0;
}
