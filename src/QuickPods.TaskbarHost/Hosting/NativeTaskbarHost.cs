using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.Contracts;
using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Interop;

namespace QuickPods.TaskbarHost.Hosting;

internal sealed class NativeTaskbarHost : IDisposable
{
    private readonly ConcurrentQueue<HostInteractionEnvelope> pendingInteractions = new();
    private readonly ConcurrentQueue<NativeLayoutInvalidationReason> pendingInvalidations = new();
    private readonly NativeSliderInteractionSession sliderSession =
        new(TaskbarSurfaceMode.Native);
    private GCHandle instanceHandle;
    private Exception? pendingWindowFailure;
    private nint windowHandle;
    private nint expectedParent;
    private PixelRect expectedBounds;
    private uint expectedDpi;
    private int ownerThreadId;
    private bool requiresRevalidation;
    private bool disposed;

    internal event Action<HostInteractionEnvelope>? Interaction;

    internal event Action<NativeLayoutInvalidationReason>? LayoutInvalidated;

    internal nint WindowHandle => windowHandle;

    internal bool IsCreated => windowHandle != nint.Zero;

    internal NativeHostCreationSnapshot Create(
        nint taskbarHandle,
        PixelRect screenBounds,
        uint dpi,
        TaskbarStateSnapshot initialState) =>
        CreateCore(taskbarHandle, screenBounds, dpi, initialState, showAfterValidation: true);

    internal NativeHostCreationSnapshot CreateHidden(
        nint taskbarHandle,
        PixelRect screenBounds,
        uint dpi,
        TaskbarStateSnapshot initialState) =>
        CreateCore(taskbarHandle, screenBounds, dpi, initialState, showAfterValidation: false);

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
                throw new InvalidOperationException("The taskbar layout must be rediscovered before the host can be shown.");
            }

            ValidateAttachment();
            _ = HostNativeMethods.ShowWindow(windowHandle, HostNativeMethods.ShowWindowNoActivate);
            ValidateAttachment();
            if (!TaskbarNativeMethods.IsWindowVisible(windowHandle))
            {
                throw new InvalidOperationException("The verified taskbar host did not become visible.");
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
        NativeTaskbarHost? target = null;
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
                target = instance.Target as NativeTaskbarHost;
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

    private NativeHostCreationSnapshot CreateCore(
        nint taskbarHandle,
        PixelRect screenBounds,
        uint dpi,
        TaskbarStateSnapshot initialState,
        bool showAfterValidation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The raw taskbar host requires Windows.");
        }

        if (IsCreated || instanceHandle.IsAllocated)
        {
            throw new InvalidOperationException("This native taskbar host has already been created.");
        }

        if (taskbarHandle == nint.Zero || !TaskbarNativeMethods.IsWindow(taskbarHandle))
        {
            throw new ArgumentException("The supplied taskbar HWND is not live.", nameof(taskbarHandle));
        }

        if (!screenBounds.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(screenBounds), "The host rectangle must be valid.");
        }

        if (dpi == 0 || TaskbarNativeMethods.GetDpiForWindow(taskbarHandle) != dpi)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "The expected DPI must match the taskbar HWND.");
        }

        if (!TaskbarNativeMethods.IsWindowVisible(taskbarHandle) ||
            !NativeWindowVerifier.IsUncloaked(taskbarHandle) ||
            !NativeWindowVerifier.ReadScreenBounds(taskbarHandle).Contains(screenBounds))
        {
            throw new ArgumentException(
                "The supplied host rectangle is not contained by a visible, uncloaked taskbar.",
                nameof(screenBounds));
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
            registration.TaskbarViewClassName,
                "QuickPods taskbar audio control",
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

            VerifyInitiallyHidden(windowHandle);
            AttachPopupPreserved(windowHandle, taskbarHandle);
            NativeWindowVerifier.ApplyAndVerifyColorKey(windowHandle);
            PlaceInParentCoordinates(windowHandle, taskbarHandle, screenBounds);

            expectedParent = taskbarHandle;
            expectedBounds = screenBounds;
            expectedDpi = dpi;
            ValidateAttachment();
            ThrowIfRevalidationRequired();
            ThrowIfWindowFailed();

            uint style = NativeWindowVerifier.ReadWindowLong(windowHandle, HostNativeMethods.GwlStyle);
            uint extendedStyle = NativeWindowVerifier.ReadWindowLong(
                windowHandle,
                HostNativeMethods.GwlExtendedStyle);
            var creation = new NativeHostCreationSnapshot(
                windowHandle,
                taskbarHandle,
                screenBounds,
            NativeWindowVerifier.ReadScreenBounds(windowHandle),
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
                    "Native host creation failed and cleanup also failed.",
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
            case HostNativeMethods.WmMouseMove when sliderSession.IsDragging:
                HandlePointerMove(lParam);
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
        if (!NativeWindowVerifier.TryReadSliderLayout(window, out SliderLayout layout) ||
            !NativeSliderInteractionSession.CanBegin(layout, lParam))
        {
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

    private void EnqueueInteraction(NativeSliderInteractionResult interaction)
    {
        if (interaction.StateChanged)
        {
            _ = HostNativeMethods.InvalidateRect(windowHandle, nint.Zero, false);
        }

        pendingInteractions.Enqueue(interaction.Envelope);
    }

    private void ValidateAttachment()
    {
        if (windowHandle == nint.Zero || !TaskbarNativeMethods.IsWindow(windowHandle))
        {
            throw new InvalidOperationException("The native host HWND is no longer live.");
        }

        if (expectedParent == nint.Zero || !TaskbarNativeMethods.IsWindow(expectedParent) ||
            !TaskbarNativeMethods.IsWindowVisible(expectedParent) ||
            HostNativeMethods.GetParent(windowHandle) != expectedParent)
        {
            throw new InvalidOperationException("The native host is no longer attached to its verified taskbar.");
        }

        NativeWindowVerifier.VerifyIdentity(windowHandle, Win32TaskbarDiscovery.HostViewClassName);
        uint style = NativeWindowVerifier.ReadWindowLong(windowHandle, HostNativeMethods.GwlStyle);
        uint extendedStyle = NativeWindowVerifier.ReadWindowLong(windowHandle, HostNativeMethods.GwlExtendedStyle);
        if (!NativeWindowStyles.MatchesPopupPreserved(style) ||
            !NativeWindowStyles.MatchesRequiredExtendedStyle(extendedStyle))
        {
            throw new InvalidOperationException("The taskbar host lost its popup-preserved style contract.");
        }

        if (NativeWindowVerifier.ReadScreenBounds(windowHandle) != expectedBounds)
        {
            throw new InvalidOperationException("The taskbar host moved outside its verified rectangle.");
        }

        if (!NativeWindowVerifier.ReadScreenBounds(expectedParent).Contains(expectedBounds))
        {
            throw new InvalidOperationException("The verified host rectangle is no longer contained by the taskbar.");
        }

        if (TaskbarNativeMethods.GetDpiForWindow(expectedParent) != expectedDpi ||
            TaskbarNativeMethods.GetDpiForWindow(windowHandle) != expectedDpi)
        {
            throw new InvalidOperationException("The taskbar host DPI no longer matches its verified parent.");
        }

        if (!NativeWindowVerifier.IsUncloaked(expectedParent) ||
            !NativeWindowVerifier.IsUncloaked(windowHandle))
        {
            throw new InvalidOperationException("The taskbar host or its parent is cloaked.");
        }
    }

    private static void VerifyInitiallyHidden(nint window)
    {
        if (TaskbarNativeMethods.IsWindowVisible(window) || HostNativeMethods.GetParent(window) != nint.Zero)
        {
            throw new InvalidOperationException("The taskbar view must start hidden and top-level.");
        }

        NativeWindowVerifier.VerifyIdentity(window, Win32TaskbarDiscovery.HostViewClassName);
        if (!NativeWindowStyles.MatchesPopupPreserved(
                NativeWindowVerifier.ReadWindowLong(window, HostNativeMethods.GwlStyle)) ||
            !NativeWindowStyles.MatchesRequiredExtendedStyle(
                NativeWindowVerifier.ReadWindowLong(window, HostNativeMethods.GwlExtendedStyle)))
        {
            throw new InvalidOperationException("The initial taskbar view styles are unsafe.");
        }
    }

    private static void AttachPopupPreserved(nint window, nint taskbar)
    {
        HostNativeMethods.SetLastError(0);
        nint previousParent = HostNativeMethods.SetParent(window, taskbar);
        int error = Marshal.GetLastWin32Error();
        if (previousParent == nint.Zero && error != 0)
        {
            throw new Win32Exception(error);
        }

        if (HostNativeMethods.GetParent(window) != taskbar ||
            !NativeWindowStyles.MatchesPopupPreserved(
                NativeWindowVerifier.ReadWindowLong(window, HostNativeMethods.GwlStyle)))
        {
            throw new InvalidOperationException("SetParent did not preserve the verified WS_POPUP attachment.");
        }
    }

    private static void PlaceInParentCoordinates(
        nint window,
        nint taskbar,
        PixelRect screenBounds)
    {
        var point = new HostNativeMethods.NativePoint(screenBounds.Left, screenBounds.Top);
        HostNativeMethods.SetLastError(0);
        int mapped = HostNativeMethods.MapWindowPoints(nint.Zero, taskbar, ref point, 1);
        if (mapped == 0 && Marshal.GetLastWin32Error() != 0)
        {
            throw new Win32Exception();
        }

        if (!HostNativeMethods.SetWindowPosition(
                window,
                nint.Zero,
                point.X,
                point.Y,
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
            throw new InvalidOperationException("The taskbar host placement was not honored exactly.");
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
                ? new InvalidOperationException("The native taskbar host HWND could not be destroyed.")
                : new Win32Exception(error, "The native taskbar host HWND could not be destroyed.");
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
        expectedParent = nint.Zero;
        expectedBounds = default;
        expectedDpi = 0;
        sliderSession.Reset();
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
            throw new InvalidOperationException("The native taskbar host has not been created.");
        }

        if (ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Native window operations must run on the HWND owner thread.");
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
            throw new InvalidOperationException("The native window procedure failed.", failure);
        }
    }

    private void ThrowIfRevalidationRequired()
    {
        if (requiresRevalidation)
        {
            throw new InvalidOperationException(
                "The taskbar layout changed while the native host was being created.");
        }
    }

}
