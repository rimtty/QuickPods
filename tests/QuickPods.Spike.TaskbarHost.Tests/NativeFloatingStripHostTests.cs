using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;
using QuickPods.Spike.TaskbarHost.Interop;
using Xunit.Sdk;

namespace QuickPods.Spike.TaskbarHost.Tests;

[Collection(NativeHostTestGroup.Name)]
public sealed class NativeFloatingStripHostTests
{
    private const int OffscreenLeft = -11600;
    private const int OffscreenTop = -11600;

    [Fact]
    public void Create_places_unowned_nontopmost_strip_and_destroy_removes_the_only_window()
    {
        RequireWindowTest();

        PixelRect requested = CreateBounds();
        using var host = new NativeFloatingStripHost();

        NativeFloatingHostCreationSnapshot snapshot = CreateStableHost(host, requested);
        nint view = snapshot.WindowHandle;

        Assert.NotEqual(nint.Zero, view);
        Assert.Equal(nint.Zero, NativeMethods.GetParent(view));
        Assert.Equal(nint.Zero, NativeMethods.GetWindow(view, NativeConstants.GetWindowOwner));
        Assert.Equal(requested, snapshot.RequestedScreenBounds);
        Assert.Equal(requested, snapshot.ActualScreenBounds);
        Assert.Equal(snapshot.ExpectedDpi, snapshot.DpiAwareness.WindowDpi);
        Assert.True(NativeWindowStyles.MatchesFloatingWindow(snapshot.Style, snapshot.ExtendedStyle));
        Assert.Equal(0, snapshot.Style & NativeConstants.WindowStyleChild);
        Assert.Equal(0, snapshot.ExtendedStyle & NativeConstants.WindowExtendedStyleTopmost);
        Assert.True(snapshot.DpiAwareness.WindowDpi > 0);
        Assert.True(NativeMethods.GetLayeredWindowAttributes(
            view,
            out uint transparentColorKey,
            out _,
            out uint layeredFlags));
        Assert.Equal(NativeConstants.TransparentColorKey, transparentColorKey);
        Assert.NotEqual(0u, layeredFlags & NativeConstants.LayeredWindowAttributeColorKey);
        Assert.Equal(
            NativeConstants.MouseActivateNoActivate,
            NativeMethods.SendMessage(view, NativeConstants.WmMouseActivate, 0, nint.Zero));
        Assert.True(NativeMethods.IsWindowVisible(view));
        Assert.True(host.IsCurrentPlacementValid());

        host.Hide();
        Assert.False(NativeMethods.IsWindowVisible(view));
        host.Show();
        Assert.True(NativeMethods.IsWindowVisible(view));
        Assert.True(NativeMethods.UpdateWindow(view));
        Assert.Equal(
            NativeConstants.TransparentColorKey,
            ReadWindowPixel(view, 0, 0));

        host.Destroy();

        Assert.False(NativeMethods.IsWindow(view));
        Assert.False(host.IsCreated);
        host.Destroy();
        Assert.False(host.IsCreated);
    }

    [Fact]
    public void Pointer_and_wheel_messages_use_the_same_queued_interaction_contract()
    {
        RequireWindowTest();

        PixelRect requested = CreateBounds();
        using var host = new NativeFloatingStripHost();
        NativeFloatingHostCreationSnapshot snapshot = CreateStableHost(host, requested);
        Assert.True(SliderGeometry.TryCreate(requested.Width, requested.Height, out SliderLayout layout));
        var observed = new List<NativeHostInteraction>();
        host.Interaction += observed.Add;

        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmLeftButtonDown,
            0,
            MakePointParameter(5, 5));
        _ = host.PumpMessages();
        Assert.Empty(observed);
        Assert.NotEqual(snapshot.WindowHandle, NativeMethods.GetCapture());

        int firstX = layout.TrackLeft;
        int movedX = layout.TrackLeft + ((layout.TrackRight - layout.TrackLeft) / 2);
        int finalX = layout.TrackRight;
        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmLeftButtonDown,
            0,
            MakePointParameter(firstX, layout.CenterY));
        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmMouseMove,
            0,
            MakePointParameter(movedX, layout.CenterY));
        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmLeftButtonUp,
            0,
            MakePointParameter(finalX, layout.CenterY));
        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmMouseWheel,
            MakeWheelParameter(120),
            MakePointParameter(-120, 240));

        Assert.Empty(observed);
        _ = host.PumpMessages();

        Assert.Collection(
            observed,
            value => AssertInteraction(
                value,
                NativeInteractionKind.DragStarted,
                firstX,
                layout.CenterY,
                0,
                false),
            value => AssertInteraction(
                value,
                NativeInteractionKind.DragMoved,
                movedX,
                layout.CenterY,
                0,
                false),
            value => AssertInteraction(
                value,
                NativeInteractionKind.DragCompleted,
                finalX,
                layout.CenterY,
                0,
                false),
            value => AssertInteraction(
                value,
                NativeInteractionKind.Wheel,
                -120,
                240,
                120,
                true));

        observed.Clear();
        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmLeftButtonDown,
            0,
            MakePointParameter(firstX, layout.CenterY));
        Assert.True(NativeMethods.ReleaseCapture());
        Assert.Empty(observed);

        _ = host.PumpMessages();

        Assert.Collection(
            observed,
            value => AssertInteraction(
                value,
                NativeInteractionKind.DragStarted,
                firstX,
                layout.CenterY,
                0,
                false),
            value => AssertInteraction(
                value,
                NativeInteractionKind.CaptureLost,
                0,
                0,
                0,
                false));
    }

    [Fact]
    public void Dpi_invalidation_hides_before_queued_event_dispatch()
    {
        RequireWindowTest();

        using var host = new NativeFloatingStripHost();
        NativeFloatingHostCreationSnapshot snapshot = CreateStableHost(host, CreateBounds());
        var observed = new List<NativeLayoutInvalidationReason>();
        host.LayoutInvalidated += observed.Add;

        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmDpiChanged,
            0,
            nint.Zero);

        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        Assert.Empty(observed);
        _ = Assert.Throws<NativeLayoutInvalidatedException>(host.Show);
        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));

        _ = host.PumpMessages();

        Assert.Equal(new[] { NativeLayoutInvalidationReason.DpiChanged }, observed);
        _ = Assert.Throws<NativeLayoutInvalidatedException>(host.Show);
        host.ShowAtVerifiedBounds(snapshot.RequestedScreenBounds, snapshot.ExpectedDpi);
        Assert.True(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
    }

    [Fact]
    public void Show_fails_closed_after_external_geometry_drift()
    {
        RequireWindowTest();

        PixelRect requested = CreateBounds();
        using var host = new NativeFloatingStripHost();
        NativeFloatingHostCreationSnapshot snapshot = CreateStableHost(host, requested);
        host.Hide();
        Assert.True(NativeMethods.SetWindowPos(
            snapshot.WindowHandle,
            nint.Zero,
            requested.Left + 1,
            requested.Top,
            requested.Width,
            requested.Height,
            NativeConstants.SetWindowPositionNoZOrder |
            NativeConstants.SetWindowPositionNoOwnerZOrder |
            NativeConstants.SetWindowPositionNoActivate));

        _ = Assert.Throws<InvalidOperationException>(host.Show);

        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        Assert.True(NativeMethods.IsWindow(snapshot.WindowHandle));
    }

    [Fact]
    public void Show_fails_closed_after_external_style_corruption()
    {
        RequireWindowTest();

        using var host = new NativeFloatingStripHost();
        NativeFloatingHostCreationSnapshot snapshot = CreateStableHost(host, CreateBounds());
        host.Hide();
        _ = NativeMethods.SetWindowLongPointer(
            snapshot.WindowHandle,
            NativeConstants.GwlExtendedStyle,
            nint.Zero);

        _ = Assert.Throws<InvalidOperationException>(host.Show);

        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        Assert.True(NativeMethods.IsWindow(snapshot.WindowHandle));
    }

    [Fact]
    public void Repeated_buffered_paints_release_GDI_and_USER_handles()
    {
        RequireWindowTest();

        using var host = new NativeFloatingStripHost();
        NativeFloatingHostCreationSnapshot snapshot = CreateStableHost(host, CreateBounds());
        host.VolumeFraction = 0.5;
        Assert.True(NativeMethods.UpdateWindow(snapshot.WindowHandle));
        uint baselineGdi = NativeMethods.GetGuiResources(
            NativeMethods.GetCurrentProcess(),
            NativeConstants.GuiResourceGdiObjects);
        uint baselineUser = NativeMethods.GetGuiResources(
            NativeMethods.GetCurrentProcess(),
            NativeConstants.GuiResourceUserObjects);

        for (int index = 0; index < 1000; index++)
        {
            host.VolumeFraction = index / 999d;
            Assert.True(NativeMethods.UpdateWindow(snapshot.WindowHandle));
        }

        uint afterGdi = NativeMethods.GetGuiResources(
            NativeMethods.GetCurrentProcess(),
            NativeConstants.GuiResourceGdiObjects);
        uint afterUser = NativeMethods.GetGuiResources(
            NativeMethods.GetCurrentProcess(),
            NativeConstants.GuiResourceUserObjects);
        Assert.True(
            afterGdi <= baselineGdi + 1,
            $"GDI handles grew from {baselineGdi} to {afterGdi}.");
        Assert.True(
            afterUser <= baselineUser + 1,
            $"USER handles grew from {baselineUser} to {afterUser}.");
    }

    [Fact]
    public void Create_with_mismatched_verified_monitor_dpi_never_shows_and_leaves_no_window()
    {
        RequireWindowTest();

        PixelRect requested = CreateBounds();
        uint actualDpi = ReadExpectedDpi(requested);
        uint mismatchedDpi = checked(actualDpi + 1);
        NativeWindowClassRegistry.FloatingRegistration registration =
            NativeWindowClassRegistry.GetFloatingRegistration();
        Assert.Equal(
            nint.Zero,
            NativeMethods.FindWindow(registration.ViewClassName, NativeFloatingStripHost.WindowTitle));
        using var host = new NativeFloatingStripHost();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => host.CreateForStableDpiTest(requested, mismatchedDpi));

        Assert.Contains("verified monitor DPI", failure.Message, StringComparison.Ordinal);
        Assert.False(host.IsCreated);
        Assert.Equal(nint.Zero, host.WindowHandle);
        Assert.Equal(
            nint.Zero,
            NativeMethods.FindWindow(registration.ViewClassName, NativeFloatingStripHost.WindowTitle));
    }

    [Fact]
    public void Revalidation_with_mismatched_monitor_dpi_remains_hidden_until_corrected()
    {
        RequireWindowTest();

        using var host = new NativeFloatingStripHost();
        NativeFloatingHostCreationSnapshot snapshot = CreateStableHost(host, CreateBounds());
        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmDisplayChange,
            0,
            nint.Zero);
        _ = host.PumpMessages();
        uint mismatchedDpi = checked(snapshot.ExpectedDpi + 1);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => host.ShowAtVerifiedBounds(snapshot.RequestedScreenBounds, mismatchedDpi));

        Assert.Contains("verified monitor DPI", failure.Message, StringComparison.Ordinal);
        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        _ = Assert.Throws<NativeLayoutInvalidatedException>(host.Show);

        host.ShowAtVerifiedBounds(snapshot.RequestedScreenBounds, snapshot.ExpectedDpi);

        Assert.True(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        Assert.True(host.IsCurrentPlacementValid());
    }

    private static PixelRect CreateBounds() =>
        new(OffscreenLeft, OffscreenTop, OffscreenLeft + 300, OffscreenTop + 40);

    private static NativeFloatingHostCreationSnapshot CreateStableHost(
        NativeFloatingStripHost host,
        PixelRect screenBounds) =>
        host.CreateForStableDpiTest(screenBounds, ReadExpectedDpi(screenBounds));

    private static uint ReadExpectedDpi(PixelRect screenBounds)
    {
        var nativeBounds = new NativeRect
        {
            Left = screenBounds.Left,
            Top = screenBounds.Top,
            Right = screenBounds.Right,
            Bottom = screenBounds.Bottom,
        };
        nint monitor = NativeMethods.MonitorFromRect(
            ref nativeBounds,
            NativeConstants.MonitorDefaultToNearest);
        Assert.NotEqual(nint.Zero, monitor);
        int result = NativeMethods.GetDpiForMonitor(
            monitor,
            NativeConstants.MonitorDpiTypeEffective,
            out uint dpiX,
            out uint dpiY);
        Assert.True(result >= 0, $"GetDpiForMonitor failed with HRESULT 0x{result:X8}.");
        Assert.Equal(dpiX, dpiY);
        Assert.True(dpiX > 0);
        return dpiX;
    }

    private static void RequireWindowTest()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
        {
            throw SkipException.ForSkip("Native HWND tests require an interactive Windows session.");
        }
    }

    private static nint MakePointParameter(int x, int y) =>
        new(unchecked((y << 16) | (x & 0xFFFF)));

    private static nuint MakeWheelParameter(short delta) =>
        unchecked((nuint)(uint)((ushort)delta << 16));

    private static uint ReadWindowPixel(nint window, int x, int y)
    {
        nint deviceContext = NativeMethods.GetDeviceContext(window);
        Assert.NotEqual(nint.Zero, deviceContext);
        try
        {
            return NativeMethods.GetPixel(deviceContext, x, y);
        }
        finally
        {
            Assert.Equal(1, NativeMethods.ReleaseDeviceContext(window, deviceContext));
        }
    }

    private static void AssertInteraction(
        NativeHostInteraction actual,
        NativeInteractionKind expectedKind,
        int expectedX,
        int expectedY,
        int expectedWheelDelta,
        bool expectedScreenCoordinates)
    {
        Assert.Equal(expectedKind, actual.Kind);
        Assert.Equal(expectedX, actual.X);
        Assert.Equal(expectedY, actual.Y);
        Assert.Equal(expectedWheelDelta, actual.WheelDelta);
        Assert.Equal(expectedScreenCoordinates, actual.UsesScreenCoordinates);
    }
}
