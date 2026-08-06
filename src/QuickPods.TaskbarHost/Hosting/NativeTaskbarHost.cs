using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.Contracts;
using QuickPods.TaskbarHost.Discovery;
using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Interop;
using NativeRect = QuickPods.TaskbarHost.Interop.TaskbarNativeMethods.NativeRect;

namespace QuickPods.TaskbarHost.Hosting;

internal sealed class NativeTaskbarHost : IDisposable
{
    private const int ClassNameCapacity = 128;

    private readonly ConcurrentQueue<HostInteractionEnvelope> pendingInteractions = new();
    private readonly ConcurrentQueue<NativeLayoutInvalidationReason> pendingInvalidations = new();
    private GCHandle instanceHandle;
    private TaskbarStateSnapshot state = new(TaskbarSurfaceMode.Native, 0, false, null);
    private Exception? pendingWindowFailure;
    private nint windowHandle;
    private nint expectedParent;
    private PixelRect expectedBounds;
    private uint expectedDpi;
    private int ownerThreadId;
    private long nextInteractionSequence;
    private bool requiresRevalidation;
    private bool dragging;
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

        TaskbarStateSnapshot normalized = snapshot with
        {
            SurfaceMode = TaskbarSurfaceMode.Native,
            VolumePercent = Math.Clamp(snapshot.VolumePercent, 0, 100),
        };
        if (normalized == state)
        {
            return;
        }

        state = normalized;
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
            !IsUncloaked(taskbarHandle) ||
            !ReadScreenBounds(taskbarHandle).Contains(screenBounds))
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
                registration.ViewClassName,
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
            ApplyAndVerifyColorKey(windowHandle);
            PlaceInParentCoordinates(windowHandle, taskbarHandle, screenBounds);

            expectedParent = taskbarHandle;
            expectedBounds = screenBounds;
            expectedDpi = dpi;
            requiresRevalidation = false;
            ValidateAttachment();
            ThrowIfWindowFailed();

            uint style = ReadWindowLong(windowHandle, HostNativeMethods.GwlStyle);
            uint extendedStyle = ReadWindowLong(windowHandle, HostNativeMethods.GwlExtendedStyle);
            var creation = new NativeHostCreationSnapshot(
                windowHandle,
                taskbarHandle,
                screenBounds,
                ReadScreenBounds(windowHandle),
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
                QuickPodsGdiRenderer.Paint(window, state);
                return nint.Zero;
            case HostNativeMethods.WmEraseBackground:
                return new nint(1);
            case HostNativeMethods.WmMouseActivate:
                return HostNativeMethods.MouseActivateNoActivate;
            case HostNativeMethods.WmLeftButtonDown:
                return OnLeftButtonDown(window, lParam);
            case HostNativeMethods.WmMouseMove when dragging:
                EmitPointerInteraction(HostInteractionKind.SetVolumePreview, lParam);
                return nint.Zero;
            case HostNativeMethods.WmLeftButtonUp when dragging:
                dragging = false;
                EmitPointerInteraction(HostInteractionKind.SetVolumeCommit, lParam);
                _ = HostNativeMethods.ReleaseCapture();
                return nint.Zero;
            case HostNativeMethods.WmCaptureChanged when dragging:
                dragging = false;
                return nint.Zero;
            case HostNativeMethods.WmMouseWheel:
                int wheelDelta = SignedHighWord(unchecked((nint)wParam));
                int wheelPercent = HostInteractionCalculator.VolumePercentFromWheel(
                    state.VolumePercent,
                    wheelDelta);
                EmitVolumeInteraction(HostInteractionKind.SetVolumeCommit, wheelPercent);
                return nint.Zero;
            default:
                return HostNativeMethods.DefWindowProcedure(window, message, wParam, lParam);
        }
    }

    private nint OnLeftButtonDown(nint window, nint lParam)
    {
        int pointerX = SignedLowWord(lParam);
        int pointerY = SignedHighWord(lParam);
        if (!TryReadSliderLayout(window, out SliderLayout layout) ||
            !SliderGeometry.ContainsPointer(layout, pointerX, pointerY))
        {
            return nint.Zero;
        }

        dragging = true;
        _ = HostNativeMethods.SetCapture(window);
        EmitVolumeInteraction(
            HostInteractionKind.SetVolumePreview,
            HostInteractionCalculator.VolumePercentFromPointer(layout, pointerX));
        return nint.Zero;
    }

    private void EmitPointerInteraction(HostInteractionKind kind, nint lParam)
    {
        if (!TryReadSliderLayout(windowHandle, out SliderLayout layout))
        {
            return;
        }

        int percent = HostInteractionCalculator.VolumePercentFromPointer(
            layout,
            SignedLowWord(lParam));
        EmitVolumeInteraction(kind, percent);
    }

    private void EmitVolumeInteraction(HostInteractionKind kind, int volumePercent)
    {
        if (nextInteractionSequence == long.MaxValue)
        {
            return;
        }

        int normalized = Math.Clamp(volumePercent, 0, 100);
        state = state with { VolumePercent = normalized };
        _ = HostNativeMethods.InvalidateRect(windowHandle, nint.Zero, false);
        pendingInteractions.Enqueue(new HostInteractionEnvelope(
            QuickPodsProtocol.Version,
            nextInteractionSequence++,
            kind,
            normalized));
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

        VerifyWindowIdentity(windowHandle);
        uint style = ReadWindowLong(windowHandle, HostNativeMethods.GwlStyle);
        uint extendedStyle = ReadWindowLong(windowHandle, HostNativeMethods.GwlExtendedStyle);
        if (!NativeWindowStyles.MatchesPopupPreserved(style) ||
            !NativeWindowStyles.MatchesRequiredExtendedStyle(extendedStyle))
        {
            throw new InvalidOperationException("The taskbar host lost its popup-preserved style contract.");
        }

        if (ReadScreenBounds(windowHandle) != expectedBounds)
        {
            throw new InvalidOperationException("The taskbar host moved outside its verified rectangle.");
        }

        if (!ReadScreenBounds(expectedParent).Contains(expectedBounds))
        {
            throw new InvalidOperationException("The verified host rectangle is no longer contained by the taskbar.");
        }

        if (TaskbarNativeMethods.GetDpiForWindow(expectedParent) != expectedDpi ||
            TaskbarNativeMethods.GetDpiForWindow(windowHandle) != expectedDpi)
        {
            throw new InvalidOperationException("The taskbar host DPI no longer matches its verified parent.");
        }

        if (!IsUncloaked(expectedParent) || !IsUncloaked(windowHandle))
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

        VerifyWindowIdentity(window);
        if (!NativeWindowStyles.MatchesPopupPreserved(ReadWindowLong(window, HostNativeMethods.GwlStyle)) ||
            !NativeWindowStyles.MatchesRequiredExtendedStyle(
                ReadWindowLong(window, HostNativeMethods.GwlExtendedStyle)))
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
            !NativeWindowStyles.MatchesPopupPreserved(ReadWindowLong(window, HostNativeMethods.GwlStyle)))
        {
            throw new InvalidOperationException("SetParent did not preserve the verified WS_POPUP attachment.");
        }
    }

    private static void ApplyAndVerifyColorKey(nint window)
    {
        uint expectedColorKey = TaskbarRenderTheme.Dark.TransparentColorKey;
        if (!HostNativeMethods.SetLayeredWindowAttributes(
                window,
                expectedColorKey,
                HostNativeMethods.FullOpacity,
                HostNativeMethods.LayeredWindowAttributeColorKey) ||
            !HostNativeMethods.GetLayeredWindowAttributes(
                window,
                out uint actualColorKey,
                out _,
                out uint flags) ||
            actualColorKey != expectedColorKey ||
            (flags & HostNativeMethods.LayeredWindowAttributeColorKey) == 0)
        {
            throw new InvalidOperationException("The taskbar host color key could not be verified.");
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

        if (ReadScreenBounds(window) != screenBounds)
        {
            throw new InvalidOperationException("The taskbar host placement was not honored exactly.");
        }
    }

    private static void VerifyWindowIdentity(nint window)
    {
        uint threadId = TaskbarNativeMethods.GetWindowThreadProcessId(window, out uint processId);
        if (threadId == 0 || processId != (uint)Environment.ProcessId)
        {
            throw new InvalidOperationException("The taskbar host HWND is not owned by this process.");
        }

        char[] buffer = new char[ClassNameCapacity];
        int length = HostNativeMethods.GetClassName(window, buffer, buffer.Length);
        if (length <= 0 ||
            !string.Equals(
                new string(buffer, 0, length),
                Win32TaskbarDiscovery.HostViewClassName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The taskbar host HWND class does not match the trusted class.");
        }
    }

    private static bool IsUncloaked(nint window) =>
        TaskbarNativeMethods.DwmGetWindowAttribute(
            window,
            TaskbarNativeMethods.DwmWindowAttributeCloaked,
            out uint cloaked,
            sizeof(uint)) == 0 && cloaked == 0;

    private static PixelRect ReadScreenBounds(nint window)
    {
        if (!TaskbarNativeMethods.GetWindowRect(window, out NativeRect rectangle))
        {
            throw new Win32Exception();
        }

        return rectangle.ToPixelRect();
    }

    private static uint ReadWindowLong(nint window, int index)
    {
        HostNativeMethods.SetLastError(0);
        nint value = HostNativeMethods.GetWindowLongPointer(window, index);
        int error = Marshal.GetLastWin32Error();
        if (value == nint.Zero && error != 0)
        {
            throw new Win32Exception(error);
        }

        return unchecked((uint)value.ToInt64());
    }

    private static bool TryReadSliderLayout(nint window, out SliderLayout layout)
    {
        layout = default;
        return GdiNativeMethods.GetClientRect(window, out GdiNativeMethods.NativeRect client) &&
            SliderGeometry.TryCreate(
                client.Right - client.Left,
                client.Bottom - client.Top,
                out layout);
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
        if (dragging)
        {
            dragging = false;
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
        nextInteractionSequence = 0;
        requiresRevalidation = false;
        dragging = false;
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

    private static int SignedLowWord(nint value) =>
        unchecked((short)(value.ToInt64() & 0xFFFF));

    private static int SignedHighWord(nint value) =>
        unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));
}
