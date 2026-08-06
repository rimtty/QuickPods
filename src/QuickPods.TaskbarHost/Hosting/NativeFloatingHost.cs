using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.Contracts;
using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Interop;

namespace QuickPods.TaskbarHost.Hosting;

internal sealed class NativeFloatingHost : IDisposable
{
    private readonly ConcurrentQueue<HostInteractionEnvelope> pendingInteractions = new();
    private readonly ConcurrentQueue<NativeLayoutInvalidationReason> pendingInvalidations = new();
    private readonly NativeSliderInteractionSession sliderSession =
        new(TaskbarSurfaceMode.Floating);
    private readonly NativeHoverInteractionSession hoverSession = new();
    private GCHandle instanceHandle;
    private Exception? pendingWindowFailure;
    private nint windowHandle;
    private PixelRect expectedBounds;
    private PixelRect expectedWorkArea;
    private uint expectedDpi;
    private int ownerThreadId;
    private bool requiresRevalidation;
    private bool disposed;

    internal event Action<HostInteractionEnvelope>? Interaction;

    internal event Action<NativeLayoutInvalidationReason>? LayoutInvalidated;

    internal nint WindowHandle => windowHandle;

    internal bool IsCreated => windowHandle != nint.Zero;

    internal NativeFloatingHostCreationSnapshot Create(
        PixelRect screenBounds,
        PixelRect verifiedWorkArea,
        uint dpi,
        TaskbarStateSnapshot initialState) =>
        CreateCore(screenBounds, verifiedWorkArea, dpi, initialState, showAfterValidation: true);

    internal NativeFloatingHostCreationSnapshot CreateHidden(
        PixelRect screenBounds,
        PixelRect verifiedWorkArea,
        uint dpi,
        TaskbarStateSnapshot initialState) =>
        CreateCore(screenBounds, verifiedWorkArea, dpi, initialState, showAfterValidation: false);

    internal void SetState(TaskbarStateSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (IsCreated)
        {
            EnsureOwnerThread();
        }

        if (!sliderSession.SetState(snapshot))
        {
            return;
        }

        if (IsCreated)
        {
            _ = HostNativeMethods.InvalidateRect(windowHandle, nint.Zero, false);
        }
    }

    internal void Show()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOwnerThread();
        HideViewImmediately();
        try
        {
            ThrowIfWindowFailed();
            if (requiresRevalidation)
            {
                throw new InvalidOperationException(
                    "The monitor layout must be rediscovered before the floating host can be shown.");
            }

            ValidateWindow();
            PromoteWithoutActivation();
            ValidateWindow();
            if (!TaskbarNativeMethods.IsWindowVisible(windowHandle))
            {
                throw new InvalidOperationException("The verified floating host did not become visible.");
            }

            _ = HostNativeMethods.InvalidateRect(windowHandle, nint.Zero, false);
            _ = HostNativeMethods.UpdateWindow(windowHandle);
            ThrowIfWindowFailed();
        }
        catch
        {
            HideViewImmediately();
            throw;
        }
    }

    internal void Hide()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOwnerThread();
        HideViewImmediately();
    }

    internal int PumpMessages()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOwnerThread();

        int dispatched = 0;
        while (HostNativeMethods.PeekMessage(
            out HostNativeMethods.NativeMessage message,
            nint.Zero,
            0,
            0,
            HostNativeMethods.PeekMessageRemove))
        {
            if (message.Message == HostNativeMethods.WmQuit)
            {
                HostNativeMethods.PostQuitMessage(unchecked((int)message.WParam));
                break;
            }

            _ = HostNativeMethods.TranslateMessage(ref message);
            _ = HostNativeMethods.DispatchMessage(ref message);
            dispatched++;
        }

        ThrowIfWindowFailed();
        while (pendingInteractions.TryDequeue(out HostInteractionEnvelope? interaction))
        {
            Interaction?.Invoke(interaction);
        }

        while (pendingInvalidations.TryDequeue(out NativeLayoutInvalidationReason reason))
        {
            LayoutInvalidated?.Invoke(reason);
        }

        return dispatched;
    }

    internal void Destroy()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ownerThreadId != 0)
        {
            EnsureOwnerThread();
        }

        DestroyCore();
        ResetAfterDestroy();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (ownerThreadId != 0)
        {
            EnsureOwnerThread();
        }

        DestroyCore();
        ResetAfterDestroy();
        disposed = true;
        GC.SuppressFinalize(this);
    }

    internal static nint StaticWindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        NativeFloatingHost? target = null;
        try
        {
            if (message == HostNativeMethods.WmNcCreate)
            {
                HostNativeMethods.NativeCreateStruct creation =
                    Marshal.PtrToStructure<HostNativeMethods.NativeCreateStruct>(lParam);
                if (creation.CreateParameters == nint.Zero)
                {
                    return nint.Zero;
                }

                HostNativeMethods.SetLastError(0);
                nint previous = HostNativeMethods.SetWindowLongPointer(
                    window,
                    HostNativeMethods.GwlpUserData,
                    creation.CreateParameters);
                if (previous == nint.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    return nint.Zero;
                }
            }

            nint instancePointer = HostNativeMethods.GetWindowLongPointer(
                window,
                HostNativeMethods.GwlpUserData);
            if (instancePointer != nint.Zero)
            {
                var instance = GCHandle.FromIntPtr(instancePointer);
                target = instance.Target as NativeFloatingHost;
                if (target is not null)
                {
                    if (message == HostNativeMethods.WmNcCreate)
                    {
                        target.windowHandle = window;
                    }

                    nint result = target.WindowProcedure(window, message, wParam, lParam);
                    if (message == HostNativeMethods.WmNcDestroy)
                    {
                        _ = HostNativeMethods.SetWindowLongPointer(
                            window,
                            HostNativeMethods.GwlpUserData,
                            nint.Zero);
                        if (target.windowHandle == window)
                        {
                            target.windowHandle = nint.Zero;
                        }
                    }

                    return result;
                }
            }
        }
        catch (Exception exception)
        {
            target?.RecordWindowFailure(exception);
        }

        return HostNativeMethods.DefWindowProcedure(window, message, wParam, lParam);
    }

    private NativeFloatingHostCreationSnapshot CreateCore(
        PixelRect screenBounds,
        PixelRect verifiedWorkArea,
        uint dpi,
        TaskbarStateSnapshot initialState,
        bool showAfterValidation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The raw floating host requires Windows.");
        }

        if (IsCreated || instanceHandle.IsAllocated)
        {
            throw new InvalidOperationException("This native floating host has already been created.");
        }

        if (!screenBounds.IsValid || !verifiedWorkArea.Contains(screenBounds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(screenBounds),
                "The floating rectangle must be contained by the verified work area.");
        }

        if (dpi == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "The expected monitor DPI must be positive.");
        }

        ArgumentNullException.ThrowIfNull(initialState);
        NativeWindowClassRegistry.Registration registration = NativeWindowClassRegistry.GetRegistration();
        ownerThreadId = Environment.CurrentManagedThreadId;
        SetState(initialState);

        try
        {
            instanceHandle = GCHandle.Alloc(this, GCHandleType.Normal);
            nint instancePointer = GCHandle.ToIntPtr(instanceHandle);
            windowHandle = HostNativeMethods.CreateWindow(
                NativeWindowStyles.RequiredExtendedStyle,
                registration.FloatingViewClassName,
                "QuickPods floating audio control",
                NativeWindowStyles.PopupPreservedStyle,
                screenBounds.Left,
                screenBounds.Top,
                screenBounds.Width,
                screenBounds.Height,
                nint.Zero,
                nint.Zero,
                registration.Instance,
                instancePointer);
            if (windowHandle == nint.Zero)
            {
                throw new Win32Exception();
            }

            VerifyInitiallyHiddenAndUnowned(windowHandle);
            NativeWindowVerifier.ApplyAndVerifyColorKey(windowHandle);
            PlaceAndVerify(windowHandle, screenBounds);

            expectedBounds = screenBounds;
            expectedWorkArea = verifiedWorkArea;
            expectedDpi = dpi;
            ValidateWindow();
            ThrowIfRevalidationRequired();
            ThrowIfWindowFailed();

            uint style = NativeWindowVerifier.ReadWindowLong(windowHandle, HostNativeMethods.GwlStyle);
            uint extendedStyle = NativeWindowVerifier.ReadWindowLong(
                windowHandle,
                HostNativeMethods.GwlExtendedStyle);
            var creation = new NativeFloatingHostCreationSnapshot(
                windowHandle,
                screenBounds,
                NativeWindowVerifier.ReadScreenBounds(windowHandle),
                verifiedWorkArea,
                dpi,
                style,
                extendedStyle);

            if (showAfterValidation)
            {
                Show();
            }

            return creation;
        }
        catch (Exception creationFailure)
        {
            try
            {
                DestroyCore();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Floating host creation failed and cleanup also failed.",
                    creationFailure,
                    cleanupFailure);
            }

            ResetAfterDestroy();
            throw;
        }
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (TryGetInvalidationReason(message, out NativeLayoutInvalidationReason reason))
        {
            requiresRevalidation = true;
            HideViewImmediately();
            pendingInvalidations.Enqueue(reason);
            return nint.Zero;
        }

        switch (message)
        {
            case HostNativeMethods.WmPaint:
                QuickPodsGdiRenderer.Paint(window, sliderSession.State);
                return nint.Zero;
            case HostNativeMethods.WmEraseBackground:
                return new nint(1);
            case HostNativeMethods.WmMouseActivate:
                return HostNativeMethods.MouseActivateNoActivate;
            case HostNativeMethods.WmLeftButtonDown:
                return OnLeftButtonDown(window, lParam);
            case HostNativeMethods.WmRightButtonUp:
                if (sliderSession.TryOpenContextMenu(out NativeSliderInteractionResult contextMenu))
                {
                    EnqueueInteraction(contextMenu);
                }

                return nint.Zero;
            case HostNativeMethods.WmMouseMove:
                ArmHoverTracking(window);
                if (sliderSession.IsDragging)
                {
                    HandlePointerMove(lParam);
                }

                return nint.Zero;
            case HostNativeMethods.WmMouseHover:
                if (!sliderSession.IsDragging &&
                    hoverSession.TryRequestPreview() &&
                    sliderSession.TryPreviewFlyout(out NativeSliderInteractionResult preview))
                {
                    EnqueueInteraction(preview);
                }

                return nint.Zero;
            case HostNativeMethods.WmMouseLeave:
                if (hoverSession.Reset() &&
                    sliderSession.TryNotifyPointerExited(out NativeSliderInteractionResult exited))
                {
                    EnqueueInteraction(exited);
                }

                return nint.Zero;
            case HostNativeMethods.WmLeftButtonUp when sliderSession.IsDragging:
                HandlePointerComplete(lParam);
                _ = HostNativeMethods.ReleaseCapture();
                return nint.Zero;
            case HostNativeMethods.WmCaptureChanged when sliderSession.IsDragging:
                _ = sliderSession.CancelDrag();
                return nint.Zero;
            case HostNativeMethods.WmMouseWheel:
                if (sliderSession.TryWheel(wParam, out NativeSliderInteractionResult wheel))
                {
                    EnqueueInteraction(wheel);
                }

                return nint.Zero;
            default:
                return HostNativeMethods.DefWindowProcedure(window, message, wParam, lParam);
        }
    }

    private nint OnLeftButtonDown(nint window, nint lParam)
    {
        if (!NativeWindowVerifier.TryReadSliderLayout(window, out SliderLayout layout))
        {
            return nint.Zero;
        }

        if (!NativeSliderInteractionSession.CanBegin(layout, lParam))
        {
            if (sliderSession.TryInvokePrimary(
                    layout,
                    lParam,
                    out NativeSliderInteractionResult primary))
            {
                EnqueueInteraction(primary);
            }

            return nint.Zero;
        }

        _ = HostNativeMethods.SetCapture(window);
        if (HostNativeMethods.GetCapture() != window ||
            !sliderSession.TryBegin(layout, lParam, out NativeSliderInteractionResult interaction))
        {
            _ = sliderSession.CancelDrag();
            if (HostNativeMethods.GetCapture() == window)
            {
                _ = HostNativeMethods.ReleaseCapture();
            }

            return nint.Zero;
        }

        EnqueueInteraction(interaction);
        return nint.Zero;
    }

    private void HandlePointerMove(nint lParam)
    {
        if (NativeWindowVerifier.TryReadSliderLayout(windowHandle, out SliderLayout layout) &&
            sliderSession.TryMove(layout, lParam, out NativeSliderInteractionResult interaction))
        {
            EnqueueInteraction(interaction);
        }
    }

    private void HandlePointerComplete(nint lParam)
    {
        if (NativeWindowVerifier.TryReadSliderLayout(windowHandle, out SliderLayout layout) &&
            sliderSession.TryComplete(layout, lParam, out NativeSliderInteractionResult interaction))
        {
            EnqueueInteraction(interaction);
        }
        else
        {
            _ = sliderSession.CancelDrag();
        }
    }

    private void ArmHoverTracking(nint window)
    {
        if (!hoverSession.TryArm())
        {
            return;
        }

        var tracking = new HostNativeMethods.NativeTrackMouseEvent
        {
            Size = (uint)Marshal.SizeOf<HostNativeMethods.NativeTrackMouseEvent>(),
            Flags = HostNativeMethods.TrackMouseEventHover |
                HostNativeMethods.TrackMouseEventLeave,
            TrackWindow = window,
            HoverTime = HostNativeMethods.FlyoutHoverTimeMilliseconds,
        };
        if (!HostNativeMethods.TrackMouseEvent(ref tracking))
        {
            _ = hoverSession.Reset();
        }
    }

    private void EnqueueInteraction(NativeSliderInteractionResult interaction)
    {
        if (interaction.StateChanged)
        {
            _ = HostNativeMethods.InvalidateRect(windowHandle, nint.Zero, false);
        }

        HostInteractionEnvelope envelope = interaction.Envelope;
        if (envelope.Kind is
                HostInteractionKind.OpenAudioFlyout or
                HostInteractionKind.OpenContextMenu or
                HostInteractionKind.PreviewAudioFlyout or
                HostInteractionKind.TaskbarPointerExited &&
            expectedBounds.IsValid)
        {
            envelope = new HostInteractionEnvelope(
                envelope.ProtocolVersion,
                envelope.Sequence,
                envelope.Kind,
                envelope.VolumePercent,
                new TaskbarSurfaceAnchor(
                    expectedBounds.Left,
                    expectedBounds.Top,
                    expectedBounds.Right,
                    expectedBounds.Bottom));
        }

        pendingInteractions.Enqueue(envelope);
    }

    private void PromoteWithoutActivation()
    {
        if (!HostNativeMethods.SetWindowPosition(
                windowHandle,
                HostNativeMethods.WindowInsertAfterTop,
                0,
                0,
                0,
                0,
                HostNativeMethods.SetWindowPositionNoSize |
                HostNativeMethods.SetWindowPositionNoMove |
                HostNativeMethods.SetWindowPositionNoOwnerZOrder |
                HostNativeMethods.SetWindowPositionNoActivate |
                HostNativeMethods.SetWindowPositionShowWindow))
        {
            throw new Win32Exception();
        }
    }

    private void ValidateWindow()
    {
        if (windowHandle == nint.Zero || !TaskbarNativeMethods.IsWindow(windowHandle))
        {
            throw new InvalidOperationException("The floating HWND is no longer live.");
        }

        if (HostNativeMethods.GetParent(windowHandle) != nint.Zero ||
            HostNativeMethods.GetWindow(windowHandle, HostNativeMethods.GetWindowOwner) != nint.Zero)
        {
            throw new InvalidOperationException("The floating HWND acquired a parent or owner.");
        }

        NativeWindowVerifier.VerifyIdentity(
            windowHandle,
            NativeWindowClassRegistry.FloatingViewClassName);
        uint style = NativeWindowVerifier.ReadWindowLong(windowHandle, HostNativeMethods.GwlStyle);
        uint extendedStyle = NativeWindowVerifier.ReadWindowLong(
            windowHandle,
            HostNativeMethods.GwlExtendedStyle);
        if (!NativeWindowStyles.MatchesPopupPreserved(style) ||
            !NativeWindowStyles.MatchesRequiredExtendedStyle(extendedStyle))
        {
            throw new InvalidOperationException(
                "The floating HWND lost its unowned, non-topmost popup style contract.");
        }

        if (NativeWindowVerifier.ReadScreenBounds(windowHandle) != expectedBounds ||
            !expectedWorkArea.Contains(expectedBounds))
        {
            throw new InvalidOperationException("The floating HWND moved outside its verified work area.");
        }

        if (TaskbarNativeMethods.GetDpiForWindow(windowHandle) != expectedDpi)
        {
            throw new InvalidOperationException("The floating HWND DPI no longer matches its verified monitor.");
        }

        if (!NativeWindowVerifier.IsUncloaked(windowHandle))
        {
            throw new InvalidOperationException("The floating HWND is cloaked.");
        }
    }

    private static void VerifyInitiallyHiddenAndUnowned(nint window)
    {
        if (TaskbarNativeMethods.IsWindowVisible(window) ||
            HostNativeMethods.GetParent(window) != nint.Zero ||
            HostNativeMethods.GetWindow(window, HostNativeMethods.GetWindowOwner) != nint.Zero)
        {
            throw new InvalidOperationException(
                "The floating HWND must start hidden, top-level, and unowned.");
        }

        NativeWindowVerifier.VerifyIdentity(window, NativeWindowClassRegistry.FloatingViewClassName);
        if (!NativeWindowStyles.MatchesPopupPreserved(
                NativeWindowVerifier.ReadWindowLong(window, HostNativeMethods.GwlStyle)) ||
            !NativeWindowStyles.MatchesRequiredExtendedStyle(
                NativeWindowVerifier.ReadWindowLong(window, HostNativeMethods.GwlExtendedStyle)))
        {
            throw new InvalidOperationException("The initial floating HWND styles are unsafe.");
        }
    }

    private static void PlaceAndVerify(nint window, PixelRect screenBounds)
    {
        if (!HostNativeMethods.SetWindowPosition(
                window,
                nint.Zero,
                screenBounds.Left,
                screenBounds.Top,
                screenBounds.Width,
                screenBounds.Height,
                HostNativeMethods.SetWindowPositionNoZOrder |
                HostNativeMethods.SetWindowPositionNoOwnerZOrder |
                HostNativeMethods.SetWindowPositionNoActivate |
                HostNativeMethods.SetWindowPositionFrameChanged))
        {
            throw new Win32Exception();
        }

        if (NativeWindowVerifier.ReadScreenBounds(window) != screenBounds)
        {
            throw new InvalidOperationException("The floating HWND placement was not honored exactly.");
        }
    }

    private static bool TryGetInvalidationReason(
        uint message,
        out NativeLayoutInvalidationReason reason)
    {
        switch (message)
        {
            case HostNativeMethods.WmSettingChange:
                reason = NativeLayoutInvalidationReason.SettingsChanged;
                return true;
            case HostNativeMethods.WmThemeChanged:
                reason = NativeLayoutInvalidationReason.ThemeChanged;
                return true;
            case HostNativeMethods.WmDisplayChange:
                reason = NativeLayoutInvalidationReason.DisplayChanged;
                return true;
            case HostNativeMethods.WmDpiChanged:
                reason = NativeLayoutInvalidationReason.DpiChanged;
                return true;
            case HostNativeMethods.WmDpiChangedBeforeParent:
                reason = NativeLayoutInvalidationReason.DpiChangedBeforeParent;
                return true;
            case HostNativeMethods.WmDpiChangedAfterParent:
                reason = NativeLayoutInvalidationReason.DpiChangedAfterParent;
                return true;
            default:
                reason = default;
                return false;
        }
    }

    private void HideViewImmediately()
    {
        if (sliderSession.CancelDrag())
        {
            _ = HostNativeMethods.ReleaseCapture();
        }

        if (windowHandle != nint.Zero)
        {
            _ = HostNativeMethods.ShowWindow(windowHandle, HostNativeMethods.ShowWindowHide);
        }
    }

    private void DestroyCore()
    {
        nint handle = windowHandle;
        if (handle == nint.Zero)
        {
            return;
        }

        HideViewImmediately();
        if (TaskbarNativeMethods.IsWindow(handle) &&
            !HostNativeMethods.DestroyWindow(handle) &&
            TaskbarNativeMethods.IsWindow(handle))
        {
            int error = Marshal.GetLastWin32Error();
            throw error == 0
                ? new InvalidOperationException("The floating HWND could not be destroyed.")
                : new Win32Exception(error, "The floating HWND could not be destroyed.");
        }

        windowHandle = nint.Zero;
    }

    private void ResetAfterDestroy()
    {
        if (instanceHandle.IsAllocated)
        {
            instanceHandle.Free();
        }

        ownerThreadId = 0;
        expectedBounds = default;
        expectedWorkArea = default;
        expectedDpi = 0;
        sliderSession.Reset();
        _ = hoverSession.Reset();
        requiresRevalidation = false;
        pendingWindowFailure = null;
        while (pendingInteractions.TryDequeue(out _))
        {
        }

        while (pendingInvalidations.TryDequeue(out _))
        {
        }
    }

    private void EnsureOwnerThread()
    {
        if (ownerThreadId == 0)
        {
            throw new InvalidOperationException("The native floating host has not been created.");
        }

        if (ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Floating HWND operations must run on the owner thread.");
        }
    }

    private void RecordWindowFailure(Exception exception)
    {
        pendingWindowFailure ??= exception;
        HideViewImmediately();
    }

    private void ThrowIfWindowFailed()
    {
        if (pendingWindowFailure is { } failure)
        {
            throw new InvalidOperationException("The floating window procedure failed.", failure);
        }
    }

    private void ThrowIfRevalidationRequired()
    {
        if (requiresRevalidation)
        {
            throw new InvalidOperationException(
                "The monitor layout changed while the floating host was being created.");
        }
    }

}
