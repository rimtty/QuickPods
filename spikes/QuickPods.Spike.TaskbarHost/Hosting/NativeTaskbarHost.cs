using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Interop;

namespace QuickPods.Spike.TaskbarHost.Hosting;

internal sealed class NativeTaskbarHost : IDisposable
{
    private const string TaskbarCreatedMessageName = "TaskbarCreated";

    private readonly ConcurrentQueue<NativeHostInteraction> pendingInteractions = new();
    private readonly ConcurrentQueue<NativeLayoutInvalidationReason> pendingLayoutInvalidations = new();
    private int owningManagedThreadId;
    private GCHandle instanceHandle;
    private nint windowHandle;
    private nint controlWindowHandle;
    private nint expectedParentHandle;
    private PixelRect expectedScreenBounds;
    private NativeParentStyleMode expectedStyleMode;
    private NativeDpiAwarenessMeasurement expectedDpiAwareness;
    private uint expectedParentDpi;
    private uint taskbarCreatedMessage;
    private int pendingTaskbarCreatedCount;
    private bool dragging;
    private double volumeFraction = 0.42;
    private bool disposed;

    internal event Action<NativeHostInteraction>? Interaction;

    internal event Action? TaskbarCreated;

    internal event Action<NativeLayoutInvalidationReason>? LayoutInvalidated;

    internal nint WindowHandle => windowHandle;

    internal nint ControlWindowHandle => controlWindowHandle;

    internal bool IsCreated => windowHandle != nint.Zero;

    internal double VolumeFraction
    {
        get => volumeFraction;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            volumeFraction = Math.Clamp(value, 0, 1);
            if (windowHandle != nint.Zero)
            {
                _ = NativeMethods.InvalidateRect(windowHandle, nint.Zero, false);
            }
        }
    }

    internal NativeHostCreationSnapshot Create(
        nint parentHandle,
        PixelRect screenBounds,
        NativeParentStyleMode styleMode) =>
        CreateCore(parentHandle, screenBounds, styleMode, requirePerMonitorV2: true);

    /// <summary>
    /// Exercises native parenting under the test runner's known, stable DPI
    /// context. Live taskbar code must use <see cref="Create"/>, which requires
    /// an unchanged per-monitor-v2 context.
    /// </summary>
    internal NativeHostCreationSnapshot CreateForStableDpiTestParent(
        nint parentHandle,
        PixelRect screenBounds,
        NativeParentStyleMode styleMode) =>
        CreateCore(parentHandle, screenBounds, styleMode, requirePerMonitorV2: false);

    private NativeHostCreationSnapshot CreateCore(
        nint parentHandle,
        PixelRect screenBounds,
        NativeParentStyleMode styleMode,
        bool requirePerMonitorV2)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The raw taskbar host requires Windows.");
        }

        if (IsCreated || controlWindowHandle != nint.Zero || instanceHandle.IsAllocated)
        {
            throw new InvalidOperationException("This native host has already been created.");
        }

        if (parentHandle == nint.Zero || !NativeMethods.IsWindow(parentHandle))
        {
            throw new ArgumentException("The supplied taskbar parent is not a live HWND.", nameof(parentHandle));
        }

        if (!screenBounds.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(screenBounds), "The screen rectangle must have positive dimensions.");
        }

        _ = NativeWindowStyles.GetStyle(styleMode);
        owningManagedThreadId = Environment.CurrentManagedThreadId;
        NativeWindowClassRegistry.Registration registration = NativeWindowClassRegistry.GetRegistration();
        taskbarCreatedMessage = NativeMethods.RegisterWindowMessage(TaskbarCreatedMessageName);
        if (taskbarCreatedMessage == 0)
        {
            throw new Win32Exception();
        }

        instanceHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        nint instancePointer = GCHandle.ToIntPtr(instanceHandle);

        try
        {
            controlWindowHandle = CreateControlWindow(registration, instancePointer);
            windowHandle = CreateViewWindow(registration, instancePointer, screenBounds);

            NativeDpiAwarenessMeasurement beforeParenting = NativeDpiAwarenessProbe.Measure(windowHandle);
            if (styleMode == NativeParentStyleMode.Child)
            {
                // SetParent deliberately leaves styles untouched. The documented child-window path
                // therefore establishes WS_CHILD before assigning a non-desktop parent.
                ApplyAndVerifyStyles(windowHandle, styleMode);
            }

            AttachAndVerifyParent(windowHandle, parentHandle, styleMode);
            ApplyAndVerifyStyles(windowHandle, styleMode);

            if (!NativeMethods.SetLayeredWindowAttributes(
                    windowHandle,
                    NativeConstants.TransparentColorKey,
                    NativeConstants.FullOpacity,
                    NativeConstants.LayeredWindowAttributeColorKey))
            {
                throw new Win32Exception();
            }

            NativePoint parentPoint = ConvertScreenToParentPoint(parentHandle, screenBounds.Left, screenBounds.Top);
            if (!NativeMethods.SetWindowPos(
                    windowHandle,
                    nint.Zero,
                    parentPoint.X,
                    parentPoint.Y,
                    screenBounds.Width,
                    screenBounds.Height,
                    NativeConstants.SetWindowPositionNoZOrder |
                    NativeConstants.SetWindowPositionNoOwnerZOrder |
                    NativeConstants.SetWindowPositionNoActivate |
                    NativeConstants.SetWindowPositionFrameChanged))
            {
                throw new Win32Exception();
            }

            PixelRect actualBounds = ReadScreenBounds(windowHandle);
            if (actualBounds != screenBounds)
            {
                throw new InvalidOperationException(
                    $"The host placement was not honored. Requested={screenBounds}; Actual={actualBounds}.");
            }

            long finalStyle = ReadWindowLong(windowHandle, NativeConstants.GwlStyle);
            long finalExtendedStyle = ReadWindowLong(windowHandle, NativeConstants.GwlExtendedStyle);
            NativeDpiAwarenessMeasurement afterParenting = NativeDpiAwarenessProbe.Measure(windowHandle);
            uint parentDpi = NativeMethods.GetDpiForWindow(parentHandle);
            bool dpiIsSafe = requirePerMonitorV2
                ? NativeDpiAwarenessProbe.IsPerMonitorV2StableAfterParenting(
                    beforeParenting,
                    afterParenting,
                    parentDpi)
                : NativeDpiAwarenessProbe.IsStableAfterParenting(
                    beforeParenting,
                    afterParenting,
                    parentDpi);
            if (!dpiIsSafe)
            {
                throw new InvalidOperationException(
                    $"SetParent changed the expected per-monitor DPI context or produced a DPI mismatch. " +
                    $"Before={beforeParenting}; After={afterParenting}; ParentDpi={parentDpi}.");
            }

            expectedParentHandle = parentHandle;
            expectedScreenBounds = screenBounds;
            expectedStyleMode = styleMode;
            expectedDpiAwareness = afterParenting;
            expectedParentDpi = parentDpi;
            Show();
            _ = NativeMethods.InvalidateRect(windowHandle, nint.Zero, false);

            return new(
                windowHandle,
                controlWindowHandle,
                parentHandle,
                screenBounds,
                actualBounds,
                styleMode,
                finalStyle,
                finalExtendedStyle,
                beforeParenting,
                afterParenting);
        }
        catch (Exception creationFailure)
        {
            try
            {
                DestroyCreatedWindows();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Native host creation failed and one or more HWNDs could not be destroyed.",
                    creationFailure,
                    cleanupFailure);
            }

            ReleaseInstanceHandle();
            owningManagedThreadId = 0;
            taskbarCreatedMessage = 0;
            throw;
        }
    }

    internal int PumpMessages()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOwningThread();

        int dispatched = 0;
        while (NativeMethods.PeekMessage(
            out NativeMessage message,
            nint.Zero,
            0,
            0,
            NativeConstants.PeekMessageRemove))
        {
            if (message.Message == NativeConstants.WmQuit)
            {
                NativeMethods.PostQuitMessage(unchecked((int)message.WParam));
                break;
            }

            _ = NativeMethods.TranslateMessage(ref message);
            _ = NativeMethods.DispatchMessage(ref message);
            dispatched++;
        }

        DrainNotifications();
        return dispatched;
    }

    internal void Hide()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOwningThread();
        HideViewImmediately();
    }

    internal void Show()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOwningThread();
        HideViewImmediately();

        try
        {
            ThrowIfLayoutInvalidationPending();
            ValidateAttachedWindow();
            _ = NativeMethods.ShowWindow(windowHandle, NativeConstants.ShowWindowNoActivate);
            ThrowIfLayoutInvalidationPending();
            ValidateAttachedWindow();
        }
        catch
        {
            HideViewImmediately();
            throw;
        }
    }

    internal void Destroy()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (owningManagedThreadId != 0)
        {
            EnsureOwningThread();
        }

        DestroyCreatedWindows();
        ReleaseInstanceHandle();
        owningManagedThreadId = 0;
        taskbarCreatedMessage = 0;
        dragging = false;
        pendingTaskbarCreatedCount = 0;
        expectedParentHandle = nint.Zero;
        expectedScreenBounds = default;
        expectedStyleMode = default;
        expectedDpiAwareness = default;
        expectedParentDpi = 0;
        while (pendingInteractions.TryDequeue(out _))
        {
        }


        while (pendingLayoutInvalidations.TryDequeue(out _))
        {
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (owningManagedThreadId != 0 && owningManagedThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The HWND owner thread must dispose the native host.");
        }

        DestroyCreatedWindows();
        ReleaseInstanceHandle();
        disposed = true;
        GC.SuppressFinalize(this);
    }

    internal static nint StaticWindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == NativeConstants.WmNcCreate)
            {
                NativeCreateStruct creation = Marshal.PtrToStructure<NativeCreateStruct>(lParam);
                if (creation.CreateParameters == nint.Zero)
                {
                    return nint.Zero;
                }

                NativeMethods.SetLastError(0);
                nint previous = NativeMethods.SetWindowLongPointer(
                    window,
                    NativeConstants.GwlpUserData,
                    creation.CreateParameters);
                if (previous == nint.Zero && Marshal.GetLastWin32Error() != 0)
                {
                    return nint.Zero;
                }
            }

            nint instancePointer = NativeMethods.GetWindowLongPointer(window, NativeConstants.GwlpUserData);
            if (instancePointer != nint.Zero)
            {
                var handle = GCHandle.FromIntPtr(instancePointer);
                if (handle.Target is NativeTaskbarHost host)
                {
                    nint result = host.WindowProcedure(window, message, wParam, lParam);
                    if (message == NativeConstants.WmNcDestroy)
                    {
                        _ = NativeMethods.SetWindowLongPointer(window, NativeConstants.GwlpUserData, nint.Zero);
                    }

                    return result;
                }
            }
        }
        catch
        {
            // Managed exceptions must never cross the unmanaged WndProc boundary.
        }

        return NativeMethods.DefWindowProcedure(window, message, wParam, lParam);
    }

    private static nint CreateControlWindow(
        NativeWindowClassRegistry.Registration registration,
        nint instancePointer)
    {
        nint control = NativeMethods.CreateWindow(
            (uint)NativeConstants.WindowExtendedStyleToolWindow,
            registration.ControlClassName,
            "QuickPods taskbar recovery control",
            (uint)NativeConstants.WindowStylePopup,
            0,
            0,
            0,
            0,
            nint.Zero,
            nint.Zero,
            registration.Instance,
            instancePointer);
        if (control == nint.Zero)
        {
            throw new Win32Exception();
        }

        if (NativeMethods.GetParent(control) != nint.Zero || NativeMethods.IsWindowVisible(control))
        {
            _ = NativeMethods.ShowWindow(control, NativeConstants.ShowWindowHide);
            _ = NativeMethods.DestroyWindow(control);
            throw new InvalidOperationException("The TaskbarCreated control HWND must remain hidden and top-level.");
        }

        return control;
    }

    private static nint CreateViewWindow(
        NativeWindowClassRegistry.Registration registration,
        nint instancePointer,
        PixelRect screenBounds)
    {
        nint view = NativeMethods.CreateWindow(
            (uint)NativeWindowStyles.RequiredExtendedStyle,
            registration.ViewClassName,
            "QuickPods taskbar audio control",
            (uint)NativeWindowStyles.GetInitialStyle(),
            0,
            0,
            screenBounds.Width,
            screenBounds.Height,
            nint.Zero,
            nint.Zero,
            registration.Instance,
            instancePointer);
        if (view == nint.Zero)
        {
            throw new Win32Exception();
        }

        if (NativeMethods.IsWindowVisible(view))
        {
            _ = NativeMethods.ShowWindow(view, NativeConstants.ShowWindowHide);
            _ = NativeMethods.DestroyWindow(view);
            throw new InvalidOperationException("The view HWND became visible before parenting was verified.");
        }

        return view;
    }

    private static void AttachAndVerifyParent(
        nint child,
        nint parent,
        NativeParentStyleMode mode)
    {
        NativeMethods.SetLastError(0);
        nint previousParent = NativeMethods.SetParent(child, parent);
        int error = Marshal.GetLastWin32Error();
        if (previousParent == nint.Zero && error != 0)
        {
            throw new Win32Exception(error);
        }

        if (NativeMethods.GetAncestor(child, NativeConstants.GetAncestorParent) != parent)
        {
            throw new InvalidOperationException("SetParent returned without establishing the requested parent HWND hierarchy.");
        }

        if (mode == NativeParentStyleMode.Child && NativeMethods.GetParent(child) != parent)
        {
            throw new InvalidOperationException("The WS_CHILD host does not report the requested parent through GetParent.");
        }
    }

    private static void ApplyAndVerifyStyles(nint window, NativeParentStyleMode mode)
    {
        long desiredStyle = NativeWindowStyles.GetStyle(mode);
        _ = SetWindowLong(window, NativeConstants.GwlStyle, desiredStyle);

        long actualStyle = ReadWindowLong(window, NativeConstants.GwlStyle);
        long extendedStyle = ReadWindowLong(window, NativeConstants.GwlExtendedStyle);
        if (!NativeWindowStyles.MatchesParentStyleMode(actualStyle, mode))
        {
            throw new InvalidOperationException($"The requested {mode} window style was not applied.");
        }

        if (!NativeWindowStyles.HasRequiredExtendedStyles(extendedStyle))
        {
            throw new InvalidOperationException("A required non-activating layered tool-window style is missing.");
        }
    }

    private static NativePoint ConvertScreenToParentPoint(nint parent, int screenX, int screenY)
    {
        var point = new NativePoint(screenX, screenY);
        NativeMethods.SetLastError(0);
        int result = NativeMethods.MapWindowPoints(nint.Zero, parent, ref point, 1);
        int error = Marshal.GetLastWin32Error();
        if (result == 0 && error != 0)
        {
            throw new Win32Exception(error);
        }

        return point;
    }

    private static PixelRect ReadScreenBounds(nint window)
    {
        if (!NativeMethods.GetWindowRect(window, out NativeRect rectangle))
        {
            throw new Win32Exception();
        }

        return new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
    }

    private static long SetWindowLong(nint window, int index, long value)
    {
        NativeMethods.SetLastError(0);
        nint previous = NativeMethods.SetWindowLongPointer(window, index, new nint(value));
        int error = Marshal.GetLastWin32Error();
        if (previous == nint.Zero && error != 0)
        {
            throw new Win32Exception(error);
        }

        return previous.ToInt64();
    }

    private static long ReadWindowLong(nint window, int index)
    {
        NativeMethods.SetLastError(0);
        nint value = NativeMethods.GetWindowLongPointer(window, index);
        int error = Marshal.GetLastWin32Error();
        if (value == nint.Zero && error != 0)
        {
            throw new Win32Exception(error);
        }

        return value.ToInt64();
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == taskbarCreatedMessage && window == controlWindowHandle)
        {
            HideViewImmediately();
            pendingLayoutInvalidations.Enqueue(NativeLayoutInvalidationReason.TaskbarCreated);
            Interlocked.Increment(ref pendingTaskbarCreatedCount);
            return nint.Zero;
        }

        if ((window == controlWindowHandle || window == windowHandle) &&
            TryGetLayoutInvalidationReason(message, out NativeLayoutInvalidationReason reason))
        {
            HideViewImmediately();
            pendingLayoutInvalidations.Enqueue(reason);
            return nint.Zero;
        }

        if (window != windowHandle)
        {
            return NativeMethods.DefWindowProcedure(window, message, wParam, lParam);
        }

        switch (message)
        {
            case NativeConstants.WmPaint:
                QuickPodsGdiRenderer.Paint(window, volumeFraction);
                return nint.Zero;
            case NativeConstants.WmEraseBackground:
                return new nint(1);
            case NativeConstants.WmMouseActivate:
                return NativeConstants.MouseActivateNoActivate;
            case NativeConstants.WmLeftButtonDown:
                int pointerX = SignedLowWord(lParam);
                int pointerY = SignedHighWord(lParam);
                if (!TryReadSliderLayout(window, out SliderLayout sliderLayout) ||
                    !SliderGeometry.ContainsPointer(sliderLayout, pointerX, pointerY))
                {
                    return nint.Zero;
                }

                dragging = true;
                _ = NativeMethods.SetCapture(window);
                EnqueuePointerInteraction(NativeInteractionKind.DragStarted, lParam);
                return nint.Zero;
            case NativeConstants.WmMouseMove when dragging:
                EnqueuePointerInteraction(NativeInteractionKind.DragMoved, lParam);
                return nint.Zero;
            case NativeConstants.WmLeftButtonUp when dragging:
                dragging = false;
                EnqueuePointerInteraction(NativeInteractionKind.DragCompleted, lParam);
                _ = NativeMethods.ReleaseCapture();
                return nint.Zero;
            case NativeConstants.WmCaptureChanged when dragging:
                dragging = false;
                pendingInteractions.Enqueue(new(
                    NativeInteractionKind.CaptureLost,
                    0,
                    0,
                    0,
                    false));
                return nint.Zero;
            case NativeConstants.WmMouseWheel:
                pendingInteractions.Enqueue(new(
                    NativeInteractionKind.Wheel,
                    SignedLowWord(lParam),
                    SignedHighWord(lParam),
                    SignedHighWord(unchecked((nint)wParam)),
                    true));
                return nint.Zero;
            default:
                return NativeMethods.DefWindowProcedure(window, message, wParam, lParam);
        }
    }

    private static bool TryGetLayoutInvalidationReason(
        uint message,
        out NativeLayoutInvalidationReason reason)
    {
        switch (message)
        {
            case NativeConstants.WmSettingChange:
                reason = NativeLayoutInvalidationReason.SettingsChanged;
                return true;
            case NativeConstants.WmThemeChanged:
                reason = NativeLayoutInvalidationReason.ThemeChanged;
                return true;
            case NativeConstants.WmDisplayChange:
                reason = NativeLayoutInvalidationReason.DisplayChanged;
                return true;
            case NativeConstants.WmDpiChanged:
                reason = NativeLayoutInvalidationReason.DpiChanged;
                return true;
            case NativeConstants.WmDpiChangedBeforeParent:
                reason = NativeLayoutInvalidationReason.DpiChangedBeforeParent;
                return true;
            case NativeConstants.WmDpiChangedAfterParent:
                reason = NativeLayoutInvalidationReason.DpiChangedAfterParent;
                return true;
            default:
                reason = default;
                return false;
        }
    }

    private void EnqueuePointerInteraction(NativeInteractionKind kind, nint lParam)
    {
        pendingInteractions.Enqueue(new(
            kind,
            SignedLowWord(lParam),
            SignedHighWord(lParam),
            0,
            false));
    }

    private static bool TryReadSliderLayout(nint window, out SliderLayout layout)
    {
        layout = default;
        return NativeMethods.GetClientRect(window, out NativeRect client) &&
            SliderGeometry.TryCreate(client.Right - client.Left, client.Bottom - client.Top, out layout);
    }

    private void DrainNotifications()
    {
        while (pendingInteractions.TryDequeue(out NativeHostInteraction interaction))
        {
            Interaction?.Invoke(interaction);
        }

        while (pendingLayoutInvalidations.TryDequeue(out NativeLayoutInvalidationReason reason))
        {
            LayoutInvalidated?.Invoke(reason);
        }

        int taskbarNotifications = Interlocked.Exchange(ref pendingTaskbarCreatedCount, 0);
        for (int index = 0; index < taskbarNotifications; index++)
        {
            TaskbarCreated?.Invoke();
        }
    }

    private void ThrowIfLayoutInvalidationPending()
    {
        if (!pendingLayoutInvalidations.IsEmpty ||
            Volatile.Read(ref pendingTaskbarCreatedCount) != 0)
        {
            throw new NativeLayoutInvalidatedException(
                "A Shell or coordinate-context invalidation is pending; the verified host must remain hidden.");
        }
    }

    private void DestroyCreatedWindows()
    {
        Exception? viewFailure = null;
        Exception? controlFailure = null;

        try
        {
            DestroyWindow(ref windowHandle, "view");
        }
        catch (Exception exception)
        {
            viewFailure = exception;
        }

        try
        {
            DestroyWindow(ref controlWindowHandle, "control");
        }
        catch (Exception exception)
        {
            controlFailure = exception;
        }

        if (viewFailure is not null && controlFailure is not null)
        {
            throw new AggregateException("The native view and control HWNDs could not be destroyed.", viewFailure, controlFailure);
        }

        if (viewFailure is not null)
        {
            throw new AggregateException("The native view HWND could not be destroyed.", viewFailure);
        }

        if (controlFailure is not null)
        {
            throw new AggregateException("The native control HWND could not be destroyed.", controlFailure);
        }

        expectedParentHandle = nint.Zero;
        expectedScreenBounds = default;
        expectedStyleMode = default;
        expectedDpiAwareness = default;
        expectedParentDpi = 0;
    }

    private static void DestroyWindow(ref nint window, string role)
    {
        nint handle = window;
        if (handle == nint.Zero)
        {
            return;
        }

        _ = NativeMethods.ShowWindow(handle, NativeConstants.ShowWindowHide);
        if (NativeMethods.IsWindow(handle) && !NativeMethods.DestroyWindow(handle) && NativeMethods.IsWindow(handle))
        {
            int error = Marshal.GetLastWin32Error();
            throw error == 0
                ? new InvalidOperationException($"The native {role} HWND could not be destroyed.")
                : new Win32Exception(error, $"The native {role} HWND could not be destroyed.");
        }

        window = nint.Zero;
    }

    private void HideViewImmediately()
    {
        if (dragging)
        {
            dragging = false;
            pendingInteractions.Enqueue(new(
                NativeInteractionKind.CaptureLost,
                0,
                0,
                0,
                false));
            _ = NativeMethods.ReleaseCapture();
        }

        if (windowHandle != nint.Zero)
        {
            _ = NativeMethods.ShowWindow(windowHandle, NativeConstants.ShowWindowHide);
        }
    }

    private void ValidateAttachedWindow()
    {
        if (windowHandle == nint.Zero ||
            expectedParentHandle == nint.Zero ||
            !NativeMethods.IsWindow(windowHandle) ||
            !NativeMethods.IsWindow(expectedParentHandle))
        {
            throw new InvalidOperationException("The native host or its expected parent is no longer a live HWND.");
        }

        if (NativeMethods.GetAncestor(windowHandle, NativeConstants.GetAncestorParent) != expectedParentHandle)
        {
            throw new InvalidOperationException("The native host is no longer attached to its verified parent.");
        }

        if (expectedStyleMode == NativeParentStyleMode.Child &&
            NativeMethods.GetParent(windowHandle) != expectedParentHandle)
        {
            throw new InvalidOperationException("The WS_CHILD host no longer reports its verified parent through GetParent.");
        }

        PixelRect actualBounds = ReadScreenBounds(windowHandle);
        if (actualBounds != expectedScreenBounds)
        {
            throw new InvalidOperationException(
                $"The native host moved outside its verified bounds. Expected={expectedScreenBounds}; Actual={actualBounds}.");
        }

        long style = ReadWindowLong(windowHandle, NativeConstants.GwlStyle);
        long extendedStyle = ReadWindowLong(windowHandle, NativeConstants.GwlExtendedStyle);
        if (!NativeWindowStyles.MatchesParentStyleMode(style, expectedStyleMode) ||
            !NativeWindowStyles.HasRequiredExtendedStyles(extendedStyle))
        {
            throw new InvalidOperationException("The native host styles changed after placement verification.");
        }

        uint currentParentDpi = NativeMethods.GetDpiForWindow(expectedParentHandle);
        NativeDpiAwarenessMeasurement currentDpiAwareness = NativeDpiAwarenessProbe.Measure(windowHandle);
        if (currentParentDpi != expectedParentDpi ||
            currentDpiAwareness != expectedDpiAwareness ||
            currentDpiAwareness.WindowDpi != currentParentDpi)
        {
            throw new InvalidOperationException(
                "The native host DPI context changed after placement verification.");
        }
    }

    private void ReleaseInstanceHandle()
    {
        if (instanceHandle.IsAllocated)
        {
            instanceHandle.Free();
        }
    }

    private void EnsureOwningThread()
    {
        if (owningManagedThreadId == 0)
        {
            throw new InvalidOperationException("The native host has not been created.");
        }

        if (owningManagedThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Native window operations must run on the HWND owner thread.");
        }
    }

    private static int SignedLowWord(nint value) => unchecked((short)(value.ToInt64() & 0xFFFF));

    private static int SignedHighWord(nint value) => unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));
}
