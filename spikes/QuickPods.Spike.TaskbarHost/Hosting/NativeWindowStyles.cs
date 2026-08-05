using QuickPods.Spike.TaskbarHost.Interop;

namespace QuickPods.Spike.TaskbarHost.Hosting;

internal static class NativeWindowStyles
{
    private const long NativeStyleBits = 0xFFFFFFFFL;
    private const long AllowedDynamicStyle = NativeConstants.WindowStyleVisible;

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
        (style & NativeStyleBits) == RequiredExtendedStyle;

    internal static bool MatchesParentStyleMode(long style, NativeParentStyleMode mode)
    {
        if (mode is not NativeParentStyleMode.PopupPreserved and not NativeParentStyleMode.Child)
        {
            return false;
        }

        long stableStyle = (style & NativeStyleBits) & ~AllowedDynamicStyle;
        return stableStyle == GetStyle(mode);
    }

    internal static bool MatchesFloatingWindow(long style, long extendedStyle) =>
        MatchesParentStyleMode(style, NativeParentStyleMode.PopupPreserved) &&
        HasRequiredExtendedStyles(extendedStyle);
}
