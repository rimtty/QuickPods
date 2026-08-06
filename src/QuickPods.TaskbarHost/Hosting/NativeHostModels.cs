using QuickPods.TaskbarHost.Geometry;

namespace QuickPods.TaskbarHost.Hosting;

internal enum NativeLayoutInvalidationReason
{
    SettingsChanged,
    ThemeChanged,
    DisplayChanged,
    DpiChanged,
    DpiChangedBeforeParent,
    DpiChangedAfterParent,
}

internal readonly record struct NativeHostCreationSnapshot(
    nint WindowHandle,
    nint ParentHandle,
    PixelRect RequestedScreenBounds,
    PixelRect ActualScreenBounds,
    uint Dpi,
    uint Style,
    uint ExtendedStyle);

internal readonly record struct NativeFloatingHostCreationSnapshot(
    nint WindowHandle,
    PixelRect RequestedScreenBounds,
    PixelRect ActualScreenBounds,
    PixelRect VerifiedWorkArea,
    uint Dpi,
    uint Style,
    uint ExtendedStyle);

internal static class HostInteractionCalculator
{
    private const int WheelStepPercent = 2;

    internal static int VolumePercentFromPointer(SliderLayout layout, int pointerX) =>
        (int)Math.Round(
            SliderGeometry.FractionFromPointerX(layout, pointerX) * 100d,
            MidpointRounding.AwayFromZero);

    internal static int VolumePercentFromWheel(int currentPercent, int wheelDelta)
    {
        int normalizedCurrent = Math.Clamp(currentPercent, 0, 100);
        return Math.Clamp(
            normalizedCurrent + (Math.Sign(wheelDelta) * WheelStepPercent),
            0,
            100);
    }
}
