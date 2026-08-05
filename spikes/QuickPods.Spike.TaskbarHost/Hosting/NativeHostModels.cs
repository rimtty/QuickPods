using QuickPods.Spike.TaskbarHost.Geometry;

namespace QuickPods.Spike.TaskbarHost.Hosting;

internal enum NativeParentStyleMode
{
    PopupPreserved,
    Child,
}

internal enum NativeInteractionKind
{
    DragStarted,
    DragMoved,
    DragCompleted,
    CaptureLost,
    Wheel,
}

internal enum NativeLayoutInvalidationReason
{
    TaskbarCreated,
    SettingsChanged,
    ThemeChanged,
    DisplayChanged,
    DpiChanged,
    DpiChangedBeforeParent,
    DpiChangedAfterParent,
}

internal enum NativeDpiAwareness
{
    Unknown = -1,
    Unaware = 0,
    SystemAware = 1,
    PerMonitorAware = 2,
}

internal sealed class NativeLayoutInvalidatedException(string message)
    : InvalidOperationException(message);

internal readonly record struct NativeHostInteraction(
    NativeInteractionKind Kind,
    int X,
    int Y,
    int WheelDelta,
    bool UsesScreenCoordinates);

internal readonly record struct NativeDpiAwarenessMeasurement(
    NativeDpiAwareness Process,
    NativeDpiAwareness Thread,
    NativeDpiAwareness Window,
    bool ThreadIsPerMonitorV2,
    bool WindowIsPerMonitorV2,
    uint WindowDpi);

internal readonly record struct NativeHostCreationSnapshot(
    nint WindowHandle,
    nint ControlWindowHandle,
    nint ParentHandle,
    PixelRect RequestedScreenBounds,
    PixelRect ActualScreenBounds,
    NativeParentStyleMode StyleMode,
    long Style,
    long ExtendedStyle,
    NativeDpiAwarenessMeasurement BeforeParentingDpi,
    NativeDpiAwarenessMeasurement AfterParentingDpi);

internal readonly record struct NativeFloatingHostCreationSnapshot(
    nint WindowHandle,
    PixelRect RequestedScreenBounds,
    PixelRect ActualScreenBounds,
    uint ExpectedDpi,
    long Style,
    long ExtendedStyle,
    NativeDpiAwarenessMeasurement DpiAwareness);
