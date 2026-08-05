using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Hosting;
using QuickPods.Spike.TaskbarHost.Interop;
using Xunit.Sdk;

namespace QuickPods.Spike.TaskbarHost.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeHostTestGroup
{
    public const string Name = "Native HWND host";
}

[Collection(NativeHostTestGroup.Name)]
public sealed class NativeHostWindowTests
{
    [Theory]
    [InlineData((int)NativeParentStyleMode.PopupPreserved)]
    [InlineData((int)NativeParentStyleMode.Child)]
    public void Create_parents_places_and_destroys_hidden_test_host(int modeValue)
    {
        RequireWindowTest();

        var mode = (NativeParentStyleMode)modeValue;
        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();

        NativeHostCreationSnapshot snapshot = host.CreateForStableDpiTestParent(
            parent.Handle,
            requested,
            mode);
        nint view = snapshot.WindowHandle;
        nint control = snapshot.ControlWindowHandle;

        Assert.NotEqual(nint.Zero, view);
        Assert.NotEqual(nint.Zero, control);
        Assert.Equal(
            parent.Handle,
            NativeMethods.GetAncestor(view, NativeConstants.GetAncestorParent));
        if (mode == NativeParentStyleMode.Child)
        {
            Assert.Equal(parent.Handle, NativeMethods.GetParent(view));
        }

        Assert.Equal(nint.Zero, NativeMethods.GetParent(control));
        Assert.False(NativeMethods.IsWindowVisible(control));
        Assert.Equal(requested, snapshot.ActualScreenBounds);
        Assert.True(NativeWindowStyles.MatchesParentStyleMode(snapshot.Style, mode));
        Assert.True(NativeWindowStyles.HasRequiredExtendedStyles(snapshot.ExtendedStyle));
        Assert.True(NativeMethods.GetLayeredWindowAttributes(
            view,
            out uint transparentColorKey,
            out _,
            out uint layeredFlags));
        Assert.Equal(NativeConstants.TransparentColorKey, transparentColorKey);
        Assert.NotEqual(0u, layeredFlags & NativeConstants.LayeredWindowAttributeColorKey);
        Assert.True(snapshot.BeforeParentingDpi.WindowDpi > 0);
        Assert.True(snapshot.AfterParentingDpi.WindowDpi > 0);
        Assert.Equal(
            NativeConstants.MouseActivateNoActivate,
            NativeMethods.SendMessage(view, NativeConstants.WmMouseActivate, 0, nint.Zero));

        host.Hide();
        Assert.False(NativeMethods.IsWindowVisible(view));
        host.Show();
        Assert.True(NativeMethods.IsWindowVisible(view));
        Assert.True(NativeMethods.UpdateWindow(view));
        nint deviceContext = NativeMethods.GetDeviceContext(view);
        Assert.NotEqual(nint.Zero, deviceContext);
        try
        {
            Assert.Equal(
                NativeConstants.TransparentColorKey,
                NativeMethods.GetPixel(deviceContext, 0, 0));
        }
        finally
        {
            Assert.Equal(1, NativeMethods.ReleaseDeviceContext(view, deviceContext));
        }

        host.Destroy();

        Assert.False(NativeMethods.IsWindow(view));
        Assert.False(NativeMethods.IsWindow(control));
        Assert.False(host.IsCreated);
    }

    [Theory]
    [InlineData((int)NativeParentStyleMode.PopupPreserved)]
    [InlineData((int)NativeParentStyleMode.Child)]
    public void CreateHidden_prepares_verified_host_without_showing_it(int modeValue)
    {
        RequireWindowTest();

        var mode = (NativeParentStyleMode)modeValue;
        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();

        NativeHostCreationSnapshot snapshot = host.CreateHiddenForStableDpiTestParent(
            parent.Handle,
            requested,
            mode);

        Assert.NotEqual(nint.Zero, snapshot.WindowHandle);
        Assert.NotEqual(nint.Zero, snapshot.ControlWindowHandle);
        Assert.True(NativeMethods.IsWindow(snapshot.WindowHandle));
        Assert.True(NativeMethods.IsWindow(snapshot.ControlWindowHandle));
        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        Assert.False(NativeMethods.IsWindowVisible(snapshot.ControlWindowHandle));
        Assert.Equal(parent.Handle, snapshot.ParentHandle);
        Assert.Equal(requested, snapshot.RequestedScreenBounds);
        Assert.Equal(requested, snapshot.ActualScreenBounds);
        Assert.Equal(mode, snapshot.StyleMode);
        Assert.True(NativeWindowStyles.MatchesParentStyleMode(snapshot.Style, mode));
        Assert.True(NativeWindowStyles.HasRequiredExtendedStyles(snapshot.ExtendedStyle));
        Assert.True(snapshot.BeforeParentingDpi.WindowDpi > 0);
        Assert.True(snapshot.AfterParentingDpi.WindowDpi > 0);
        Assert.True(host.IsCurrentAttachmentValidWhileHidden());
        Assert.False(host.IsCurrentAttachmentValid());

        host.Show();

        Assert.True(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        Assert.True(host.IsCurrentAttachmentValid());
        Assert.False(host.IsCurrentAttachmentValidWhileHidden());

        host.Destroy();

        Assert.False(NativeMethods.IsWindow(snapshot.WindowHandle));
        Assert.False(NativeMethods.IsWindow(snapshot.ControlWindowHandle));
        Assert.False(host.IsCreated);
    }

    [Fact]
    public void Prepared_hidden_host_rejects_show_while_invalidation_is_pending_and_cleans_up()
    {
        RequireWindowTest();

        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();
        NativeHostCreationSnapshot snapshot = host.CreateHiddenForStableDpiTestParent(
            parent.Handle,
            requested,
            NativeParentStyleMode.Child);

        _ = NativeMethods.SendMessage(
            snapshot.ControlWindowHandle,
            NativeConstants.WmDisplayChange,
            0,
            nint.Zero);

        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        Assert.False(host.IsCurrentAttachmentValidWhileHidden());
        _ = Assert.Throws<NativeLayoutInvalidatedException>(host.Show);
        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));

        host.Destroy();

        Assert.False(NativeMethods.IsWindow(snapshot.WindowHandle));
        Assert.False(NativeMethods.IsWindow(snapshot.ControlWindowHandle));
        Assert.False(host.IsCreated);
    }

    [Theory]
    [InlineData((int)NativeParentStyleMode.PopupPreserved)]
    [InlineData((int)NativeParentStyleMode.Child)]
    public void CreateHidden_rejects_a_supplied_parent_that_is_not_top_level(int modeValue)
    {
        RequireWindowTest();

        using var root = NativeTestParentWindow.CreateOffscreen();
        using var nested = NativeTestParentWindow.CreateOffscreenChild(root.Handle);
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();

        _ = Assert.Throws<NativeLayoutInvalidatedException>(() =>
            host.CreateHiddenForStableDpiTestParent(
                nested.Handle,
                requested,
                (NativeParentStyleMode)modeValue));

        Assert.False(host.IsCreated);
    }

    [Fact]
    public void WndProc_queues_lightweight_interactions_until_the_message_pump_drains_them()
    {
        RequireWindowTest();

        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();
        NativeHostCreationSnapshot snapshot = host.CreateForStableDpiTestParent(
            parent.Handle,
            requested,
            NativeParentStyleMode.Child);
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

        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmLeftButtonDown,
            0,
            MakePointParameter(40, 20));
        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmMouseMove,
            0,
            MakePointParameter(90, 20));
        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmLeftButtonUp,
            0,
            MakePointParameter(120, 20));
        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmMouseWheel,
            MakeWheelParameter(120),
            MakePointParameter(-120, 240));

        Assert.Empty(observed);

        _ = host.PumpMessages();

        Assert.Collection(
            observed,
            value => AssertInteraction(value, NativeInteractionKind.DragStarted, 40, 20, 0, false),
            value => AssertInteraction(value, NativeInteractionKind.DragMoved, 90, 20, 0, false),
            value => AssertInteraction(value, NativeInteractionKind.DragCompleted, 120, 20, 0, false),
            value => AssertInteraction(value, NativeInteractionKind.Wheel, -120, 240, 120, true));

        observed.Clear();
        _ = NativeMethods.SendMessage(
            snapshot.WindowHandle,
            NativeConstants.WmLeftButtonDown,
            0,
            MakePointParameter(35, 20));
        Assert.True(NativeMethods.ReleaseCapture());
        Assert.Empty(observed);

        _ = host.PumpMessages();

        Assert.Collection(
            observed,
            value => AssertInteraction(value, NativeInteractionKind.DragStarted, 35, 20, 0, false),
            value => AssertInteraction(value, NativeInteractionKind.CaptureLost, 0, 0, 0, false));
    }

    [Fact]
    public void Hidden_control_window_queues_TaskbarCreated_outside_WndProc()
    {
        RequireWindowTest();

        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();
        NativeHostCreationSnapshot snapshot = host.CreateForStableDpiTestParent(
            parent.Handle,
            requested,
            NativeParentStyleMode.Child);
        int observed = 0;
        var invalidations = new List<NativeLayoutInvalidationReason>();
        host.TaskbarCreated += () => observed++;
        host.LayoutInvalidated += invalidations.Add;
        uint message = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        Assert.True(NativeMethods.IsWindowVisible(snapshot.WindowHandle));

        _ = NativeMethods.SendMessage(snapshot.ControlWindowHandle, message, 0, nint.Zero);

        Assert.Equal(0, observed);
        Assert.Empty(invalidations);
        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        _ = host.PumpMessages();
        Assert.Equal(1, observed);
        Assert.Equal(new[] { NativeLayoutInvalidationReason.TaskbarCreated }, invalidations);
    }

    [Theory]
    [InlineData((int)NativeParentStyleMode.PopupPreserved)]
    [InlineData((int)NativeParentStyleMode.Child)]
    public void Repeated_GDI_paints_release_every_created_handle(int modeValue)
    {
        RequireWindowTest();

        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();
        NativeHostCreationSnapshot snapshot = host.CreateForStableDpiTestParent(
            parent.Handle,
            requested,
            (NativeParentStyleMode)modeValue);
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
    public void Assigning_the_same_clamped_volume_does_not_invalidate_the_host()
    {
        RequireWindowTest();

        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();
        NativeHostCreationSnapshot snapshot = host.CreateForStableDpiTestParent(
            parent.Handle,
            requested,
            NativeParentStyleMode.Child);
        Assert.True(NativeMethods.UpdateWindow(snapshot.WindowHandle));
        Assert.False(NativeMethods.GetUpdateRect(snapshot.WindowHandle, out _, false));

        host.VolumeFraction = host.VolumeFraction;

        Assert.False(NativeMethods.GetUpdateRect(snapshot.WindowHandle, out _, false));

        host.VolumeFraction = 1;
        Assert.True(NativeMethods.GetUpdateRect(snapshot.WindowHandle, out _, false));
        Assert.True(NativeMethods.UpdateWindow(snapshot.WindowHandle));

        host.VolumeFraction = double.MaxValue;

        Assert.Equal(1, host.VolumeFraction);
        Assert.False(NativeMethods.GetUpdateRect(snapshot.WindowHandle, out _, false));
    }

    [Theory]
    [InlineData((int)NativeParentStyleMode.PopupPreserved)]
    [InlineData((int)NativeParentStyleMode.Child)]
    public void Rapid_click_to_jump_updates_present_expected_frames_without_GDI_growth(int modeValue)
    {
        RequireWindowTest();

        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();
        NativeHostCreationSnapshot snapshot = host.CreateForStableDpiTestParent(
            parent.Handle,
            requested,
            (NativeParentStyleMode)modeValue);
        Assert.True(SliderGeometry.TryCreate(requested.Width, requested.Height, out SliderLayout layout));
        host.Interaction += interaction =>
        {
            if (interaction.Kind is NativeInteractionKind.DragStarted or NativeInteractionKind.DragCompleted)
            {
                host.VolumeFraction = SliderGeometry.FractionFromPointerX(layout, interaction.X);
            }
        };

        int probeX = layout.TrackLeft + (((layout.TrackRight - layout.TrackLeft) * 3) / 4);
        host.VolumeFraction = 0;
        Assert.True(NativeMethods.UpdateWindow(snapshot.WindowHandle));
        uint emptyTrackPixel = ReadWindowPixel(snapshot.WindowHandle, probeX, layout.CenterY);
        host.VolumeFraction = 1;
        Assert.True(NativeMethods.UpdateWindow(snapshot.WindowHandle));
        uint filledTrackPixel = ReadWindowPixel(snapshot.WindowHandle, probeX, layout.CenterY);
        Assert.NotEqual(emptyTrackPixel, filledTrackPixel);

        uint baselineGdi = NativeMethods.GetGuiResources(
            NativeMethods.GetCurrentProcess(),
            NativeConstants.GuiResourceGdiObjects);
        uint baselineUser = NativeMethods.GetGuiResources(
            NativeMethods.GetCurrentProcess(),
            NativeConstants.GuiResourceUserObjects);

        for (int index = 0; index < 200; index++)
        {
            bool fillTrack = (index & 1) == 0;
            int pointerX = fillTrack ? layout.TrackRight : layout.TrackLeft;
            nint pointer = MakePointParameter(pointerX, layout.CenterY);
            _ = NativeMethods.SendMessage(
                snapshot.WindowHandle,
                NativeConstants.WmLeftButtonDown,
                0,
                pointer);
            _ = NativeMethods.SendMessage(
                snapshot.WindowHandle,
                NativeConstants.WmLeftButtonUp,
                0,
                pointer);
            _ = host.PumpMessages();
            Assert.True(NativeMethods.UpdateWindow(snapshot.WindowHandle));

            Assert.Equal(
                fillTrack ? filledTrackPixel : emptyTrackPixel,
                ReadWindowPixel(snapshot.WindowHandle, probeX, layout.CenterY));
        }

        host.VolumeFraction = 1;
        Assert.True(NativeMethods.UpdateWindow(snapshot.WindowHandle));
        Assert.Equal(
            filledTrackPixel,
            ReadWindowPixel(snapshot.WindowHandle, probeX, layout.CenterY));
        host.VolumeFraction = 0;
        Assert.True(NativeMethods.UpdateWindow(snapshot.WindowHandle));
        Assert.Equal(
            emptyTrackPixel,
            ReadWindowPixel(snapshot.WindowHandle, probeX, layout.CenterY));
        Assert.Equal(
            NativeConstants.TransparentColorKey,
            ReadWindowPixel(snapshot.WindowHandle, 0, 0));

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
    public void Show_fails_closed_when_verified_styles_are_changed_externally()
    {
        RequireWindowTest();

        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();
        NativeHostCreationSnapshot snapshot = host.CreateForStableDpiTestParent(
            parent.Handle,
            requested,
            NativeParentStyleMode.Child);
        host.Hide();
        _ = NativeMethods.SetWindowLongPointer(
            snapshot.WindowHandle,
            NativeConstants.GwlExtendedStyle,
            nint.Zero);

        Assert.False(host.IsCurrentAttachmentValid());
        _ = Assert.Throws<InvalidOperationException>(host.Show);

        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        Assert.True(NativeMethods.IsWindow(snapshot.WindowHandle));
    }

    [Theory]
    [InlineData((int)NativeParentStyleMode.PopupPreserved)]
    [InlineData((int)NativeParentStyleMode.Child)]
    public void Current_attachment_validation_detects_external_visibility_loss(int modeValue)
    {
        RequireWindowTest();

        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();
        NativeHostCreationSnapshot snapshot = host.CreateForStableDpiTestParent(
            parent.Handle,
            requested,
            (NativeParentStyleMode)modeValue);
        Assert.True(host.IsCurrentAttachmentValid());

        _ = NativeMethods.ShowWindow(snapshot.WindowHandle, NativeConstants.ShowWindowHide);

        Assert.False(host.IsCurrentAttachmentValid());
    }

    [Theory]
    [InlineData((int)NativeConstants.WmSettingChange, (int)NativeLayoutInvalidationReason.SettingsChanged)]
    [InlineData((int)NativeConstants.WmThemeChanged, (int)NativeLayoutInvalidationReason.ThemeChanged)]
    [InlineData((int)NativeConstants.WmDisplayChange, (int)NativeLayoutInvalidationReason.DisplayChanged)]
    [InlineData((int)NativeConstants.WmDpiChanged, (int)NativeLayoutInvalidationReason.DpiChanged)]
    [InlineData((int)NativeConstants.WmDpiChangedBeforeParent, (int)NativeLayoutInvalidationReason.DpiChangedBeforeParent)]
    [InlineData((int)NativeConstants.WmDpiChangedAfterParent, (int)NativeLayoutInvalidationReason.DpiChangedAfterParent)]
    public void Hidden_control_broadcast_invalidation_hides_view_before_event_dispatch(
        int messageValue,
        int reasonValue)
    {
        RequireWindowTest();

        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();
        NativeHostCreationSnapshot snapshot = host.CreateForStableDpiTestParent(
            parent.Handle,
            requested,
            NativeParentStyleMode.Child);
        var invalidations = new List<NativeLayoutInvalidationReason>();
        host.LayoutInvalidated += invalidations.Add;

        _ = NativeMethods.SendMessage(
            snapshot.ControlWindowHandle,
            unchecked((uint)messageValue),
            0,
            nint.Zero);

        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        Assert.Empty(invalidations);
        _ = Assert.Throws<NativeLayoutInvalidatedException>(host.Show);
        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        _ = host.PumpMessages();
        Assert.Equal(
            new[] { (NativeLayoutInvalidationReason)reasonValue },
            invalidations);
    }

    [Theory]
    [InlineData((int)NativeParentStyleMode.PopupPreserved)]
    [InlineData((int)NativeParentStyleMode.Child)]
    public void Destroyed_parent_prevents_verified_host_from_being_shown(int modeValue)
    {
        RequireWindowTest();

        using var parent = NativeTestParentWindow.CreateOffscreen();
        var requested = new PixelRect(
            NativeTestParentWindow.ScreenLeft + 20,
            NativeTestParentWindow.ScreenTop + 14,
            NativeTestParentWindow.ScreenLeft + 300,
            NativeTestParentWindow.ScreenTop + 62);
        using var host = new NativeTaskbarHost();
        NativeHostCreationSnapshot snapshot = host.CreateForStableDpiTestParent(
            parent.Handle,
            requested,
            (NativeParentStyleMode)modeValue);
        host.Hide();

        parent.Dispose();

        _ = Assert.Throws<InvalidOperationException>(host.Show);
        Assert.False(NativeMethods.IsWindowVisible(snapshot.WindowHandle));
        host.Destroy();
        Assert.False(host.IsCreated);
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

    private sealed class NativeTestParentWindow : IDisposable
    {
        private const string ClassName = "QuickPods.Spike.TaskbarHost.Tests.Parent.v1";
        private const int OffscreenLeft = -12000;
        private const int OffscreenTop = -12000;
        private static readonly NativeWindowProcedure WindowProcedure = DefWindowProcedure;
        private static readonly Lazy<NativeWindowClassRegistry.Registration> Registration =
            new(RegisterClass, true);

        private NativeTestParentWindow(nint handle)
        {
            Handle = handle;
        }

        internal nint Handle { get; }

        internal static int ScreenLeft => OffscreenLeft;

        internal static int ScreenTop => OffscreenTop;

        internal static NativeTestParentWindow CreateOffscreen()
        {
            NativeWindowClassRegistry.Registration registration = Registration.Value;
            nint handle = NativeMethods.CreateWindow(
                (uint)NativeConstants.WindowExtendedStyleToolWindow,
                ClassName,
                "QuickPods test parent",
                (uint)NativeConstants.WindowStylePopup,
                OffscreenLeft,
                OffscreenTop,
                640,
                80,
                nint.Zero,
                nint.Zero,
                registration.Instance,
                nint.Zero);
            if (handle == nint.Zero)
            {
                throw new Win32Exception();
            }

            _ = NativeMethods.ShowWindow(handle, NativeConstants.ShowWindowNoActivate);
            return new(handle);
        }

        internal static NativeTestParentWindow CreateOffscreenChild(nint parentHandle)
        {
            NativeWindowClassRegistry.Registration registration = Registration.Value;
            nint handle = NativeMethods.CreateWindow(
                (uint)NativeConstants.WindowExtendedStyleToolWindow,
                ClassName,
                "QuickPods nested test parent",
                (uint)NativeConstants.WindowStyleChild,
                0,
                0,
                640,
                80,
                parentHandle,
                nint.Zero,
                registration.Instance,
                nint.Zero);
            if (handle == nint.Zero)
            {
                throw new Win32Exception();
            }

            _ = NativeMethods.ShowWindow(handle, NativeConstants.ShowWindowNoActivate);
            return new(handle);
        }

        public void Dispose()
        {
            _ = NativeMethods.ShowWindow(Handle, NativeConstants.ShowWindowHide);
            if (NativeMethods.IsWindow(Handle) && !NativeMethods.DestroyWindow(Handle))
            {
                throw new Win32Exception();
            }
        }

        private static NativeWindowClassRegistry.Registration RegisterClass()
        {
            nint instance = NativeMethods.GetModuleHandle(null);
            nint procedure = Marshal.GetFunctionPointerForDelegate(WindowProcedure);
            var windowClass = new NativeWindowClass
            {
                Size = (uint)Marshal.SizeOf<NativeWindowClass>(),
                WindowProcedure = procedure,
                Instance = instance,
                ClassName = ClassName,
            };

            if (NativeMethods.RegisterClass(ref windowClass) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != NativeConstants.ErrorClassAlreadyExists)
                {
                    throw new Win32Exception(error);
                }
            }

            return new(ClassName, ClassName, instance);
        }

        private static nint DefWindowProcedure(nint window, uint message, nuint wParam, nint lParam) =>
            NativeMethods.DefWindowProcedure(window, message, wParam, lParam);
    }
}
