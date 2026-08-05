using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.Spike.TaskbarHost.Geometry;
using QuickPods.Spike.TaskbarHost.Interop;

namespace QuickPods.Spike.TaskbarHost.Hosting;

/// <summary>
/// Presents the diagnostic slider as an unowned top-level floating strip.
/// The caller owns placement policy and must supply already-validated physical
/// screen bounds.
/// </summary>
internal sealed class NativeFloatingStripHost : IDisposable
{
    internal const string WindowTitle = "QuickPods floating audio control";

    private readonly ConcurrentQueue<NativeHostInteraction> pendingInteractions = new();
    private readonly ConcurrentQueue<NativeLayoutInvalidationReason> pendingLayoutInvalidations = new();
    private int owningManagedThreadId;
    private GCHandle instanceHandle;
    private nint windowHandle;
    private PixelRect expectedScreenBounds;
    private NativeDpiAwarenessMeasurement expectedDpiAwareness;
    private uint expectedDpi;
    private bool requirePerMonitorV2;
    private int requiresLayoutRevalidation;
    private int invalidationGeneration;
    private bool dragging;
    private double volumeFraction = 0.42;
    private bool disposed;

    internal event Action<NativeHostInteraction>? Interaction;

    internal event Action<NativeLayoutInvalidationReason>? LayoutInvalidated;

    internal nint WindowHandle => windowHandle;

    internal bool IsCreated => windowHandle != nint.Zero;

    internal double VolumeFraction
    {
        get => volumeFraction;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            double nextVolumeFraction = Math.Clamp(value, 0, 1);
            if (nextVolumeFraction.Equals(volumeFraction))
            {
                return;
            }

            volumeFraction = nextVolumeFraction;
            if (windowHandle != nint.Zero)
            {
                _ = NativeMethods.InvalidateRect(windowHandle, nint.Zero, false);
            }
        }
    }

    internal NativeFloatingHostCreationSnapshot Create(PixelRect screenBounds, uint expectedDpi) =>
        CreateCore(screenBounds, expectedDpi, requirePerMonitorV2: true);

    /// <summary>
    /// Exercises the top-level presenter under the test runner's stable DPI
    /// context. Product integration must call <see cref="Create"/>, which
    /// requires a verified per-monitor-v2 context.
    /// </summary>
    internal NativeFloatingHostCreationSnapshot CreateForStableDpiTest(
        PixelRect screenBounds,
        uint expectedDpi) =>
        CreateCore(screenBounds, expectedDpi, requirePerMonitorV2: false);

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
            ValidateFloatingWindow();
            _ = NativeMethods.ShowWindow(windowHandle, NativeConstants.ShowWindowNoActivate);
            ThrowIfLayoutInvalidationPending();
            ValidateFloatingWindow();
        }
        catch
        {
            HideViewImmediately();
            throw;
        }
    }

    /// <summary>
    /// Applies a caller-verified physical screen rectangle after a display,
    /// DPI, theme, or settings invalidation and shows the strip without
    /// activation. Pending invalidation events must be drained first.
    /// </summary>
    internal void ShowAtVerifiedBounds(PixelRect verifiedScreenBounds, uint verifiedExpectedDpi)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOwningThread();
        HideViewImmediately();
        Interlocked.Exchange(ref requiresLayoutRevalidation, 1);

        try
        {
            if (!verifiedScreenBounds.IsValid)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(verifiedScreenBounds),
                    "The verified physical screen rectangle must have positive dimensions.");
            }

            if (verifiedExpectedDpi == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(verifiedExpectedDpi),
                    "The verified monitor DPI must be positive.");
            }

            if (!pendingLayoutInvalidations.IsEmpty)
            {
                throw new NativeLayoutInvalidatedException(
                    "Pending layout invalidations must be observed before applying verified floating bounds.");
            }

            int generation = Volatile.Read(ref invalidationGeneration);
            PlaceAndVerifyScreenBounds(windowHandle, verifiedScreenBounds);
            long style = ReadWindowLong(windowHandle, NativeConstants.GwlStyle);
            long extendedStyle = ReadWindowLong(windowHandle, NativeConstants.GwlExtendedStyle);
            VerifyTopLevelStyleAndOwnership(windowHandle, style, extendedStyle);

            NativeDpiAwarenessMeasurement dpiAwareness = NativeDpiAwarenessProbe.Measure(windowHandle);
            bool dpiIsSafe = requirePerMonitorV2
                ? IsVerifiedPerMonitorV2(dpiAwareness)
                : IsKnownStableDpi(dpiAwareness);
            if (!dpiIsSafe)
            {
                throw new InvalidOperationException(
                    $"The floating host did not retain the required DPI context. Measurement={dpiAwareness}.");
            }

            if (dpiAwareness.WindowDpi != verifiedExpectedDpi)
            {
                throw new InvalidOperationException(
                    $"The floating host DPI does not match the verified monitor DPI. " +
                    $"Expected={verifiedExpectedDpi}; Actual={dpiAwareness.WindowDpi}.");
            }

            if (generation != Volatile.Read(ref invalidationGeneration) ||
                !pendingLayoutInvalidations.IsEmpty)
            {
                throw new NativeLayoutInvalidatedException(
                    "The coordinate context changed while verified floating bounds were being applied.");
            }

            expectedScreenBounds = verifiedScreenBounds;
            expectedDpiAwareness = dpiAwareness;
            expectedDpi = verifiedExpectedDpi;
            Interlocked.Exchange(ref requiresLayoutRevalidation, 0);
            Show();
        }
        catch
        {
            Interlocked.Exchange(ref requiresLayoutRevalidation, 1);
            HideViewImmediately();
            throw;
        }
    }

    internal bool IsCurrentPlacementValid()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureOwningThread();

        try
        {
            ThrowIfLayoutInvalidationPending();
            ValidateFloatingWindow();
            return NativeMethods.IsWindowVisible(windowHandle);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    internal void Destroy()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (owningManagedThreadId != 0)
        {
            EnsureOwningThread();
        }

        DestroyCreatedWindow();
        ReleaseInstanceHandle();
        owningManagedThreadId = 0;
        expectedScreenBounds = default;
        expectedDpiAwareness = default;
        expectedDpi = 0;
        requirePerMonitorV2 = false;
        requiresLayoutRevalidation = 0;
        invalidationGeneration = 0;
        dragging = false;
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
            throw new InvalidOperationException("The HWND owner thread must dispose the floating host.");
        }

        DestroyCreatedWindow();
        ReleaseInstanceHandle();
        owningManagedThreadId = 0;
        expectedScreenBounds = default;
        expectedDpiAwareness = default;
        expectedDpi = 0;
        requirePerMonitorV2 = false;
        requiresLayoutRevalidation = 0;
        invalidationGeneration = 0;
        disposed = true;
        GC.SuppressFinalize(this);
    }

    internal static bool IsVerifiedPerMonitorV2(NativeDpiAwarenessMeasurement measurement) =>
        measurement.Process == NativeDpiAwareness.PerMonitorAware &&
        measurement.Thread == NativeDpiAwareness.PerMonitorAware &&
        measurement.Window == NativeDpiAwareness.PerMonitorAware &&
        measurement.ThreadIsPerMonitorV2 &&
        measurement.WindowIsPerMonitorV2 &&
        measurement.WindowDpi > 0;

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

                var creationHandle = GCHandle.FromIntPtr(creation.CreateParameters);
                if (creationHandle.Target is NativeFloatingStripHost creatingHost)
                {
                    creatingHost.windowHandle = window;
                }
            }

            nint instancePointer = NativeMethods.GetWindowLongPointer(window, NativeConstants.GwlpUserData);
            if (instancePointer != nint.Zero)
            {
                var handle = GCHandle.FromIntPtr(instancePointer);
                if (handle.Target is NativeFloatingStripHost host)
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

    private NativeFloatingHostCreationSnapshot CreateCore(
        PixelRect screenBounds,
        uint verifiedExpectedDpi,
        bool requirePerMonitorV2)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The raw floating host requires Windows.");
        }

        if (IsCreated || instanceHandle.IsAllocated)
        {
            throw new InvalidOperationException("This floating host has already been created.");
        }

        if (!screenBounds.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(screenBounds),
                "The physical screen rectangle must have positive dimensions.");
        }

        if (verifiedExpectedDpi == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(verifiedExpectedDpi),
                "The verified monitor DPI must be positive.");
        }

        NativeWindowClassRegistry.FloatingRegistration registration =
            NativeWindowClassRegistry.GetFloatingRegistration();
        owningManagedThreadId = Environment.CurrentManagedThreadId;

        try
        {
            instanceHandle = GCHandle.Alloc(this, GCHandleType.Normal);
            nint instancePointer = GCHandle.ToIntPtr(instanceHandle);
            windowHandle = CreateViewWindow(registration, instancePointer, screenBounds);
            VerifyInitiallyHiddenAndUnowned(windowHandle);
            ApplyAndVerifyLayeredColorKey(windowHandle);
            PlaceAndVerifyScreenBounds(windowHandle, screenBounds);

            long style = ReadWindowLong(windowHandle, NativeConstants.GwlStyle);
            long extendedStyle = ReadWindowLong(windowHandle, NativeConstants.GwlExtendedStyle);
            VerifyTopLevelStyleAndOwnership(windowHandle, style, extendedStyle);

            NativeDpiAwarenessMeasurement dpiAwareness = NativeDpiAwarenessProbe.Measure(windowHandle);
            bool dpiIsSafe = requirePerMonitorV2
                ? IsVerifiedPerMonitorV2(dpiAwareness)
                : IsKnownStableDpi(dpiAwareness);
            if (!dpiIsSafe)
            {
                throw new InvalidOperationException(
                    $"The floating host did not retain the required DPI context. Measurement={dpiAwareness}.");
            }

            if (dpiAwareness.WindowDpi != verifiedExpectedDpi)
            {
                throw new InvalidOperationException(
                    $"The floating host DPI does not match the verified monitor DPI. " +
                    $"Expected={verifiedExpectedDpi}; Actual={dpiAwareness.WindowDpi}.");
            }

            expectedScreenBounds = screenBounds;
            expectedDpiAwareness = dpiAwareness;
            expectedDpi = verifiedExpectedDpi;
            this.requirePerMonitorV2 = requirePerMonitorV2;
            Interlocked.Exchange(ref requiresLayoutRevalidation, 0);
            Show();
            _ = NativeMethods.InvalidateRect(windowHandle, nint.Zero, false);

            return new(
                windowHandle,
                screenBounds,
                ReadScreenBounds(windowHandle),
                verifiedExpectedDpi,
                style,
                extendedStyle,
                dpiAwareness);
        }
        catch (Exception creationFailure)
        {
            try
            {
                DestroyCreatedWindow();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "Floating host creation failed and its HWND could not be destroyed.",
                    creationFailure,
                    cleanupFailure);
            }

            ReleaseInstanceHandle();
            owningManagedThreadId = 0;
            expectedScreenBounds = default;
            expectedDpiAwareness = default;
            expectedDpi = 0;
            this.requirePerMonitorV2 = false;
            requiresLayoutRevalidation = 0;
            invalidationGeneration = 0;
            throw;
        }
    }

    private static nint CreateViewWindow(
        NativeWindowClassRegistry.FloatingRegistration registration,
        nint instancePointer,
        PixelRect screenBounds)
    {
        nint view = NativeMethods.CreateWindow(
            (uint)NativeWindowStyles.GetFloatingExtendedStyle(),
            registration.ViewClassName,
            WindowTitle,
            (uint)NativeWindowStyles.GetFloatingStyle(),
            screenBounds.Left,
            screenBounds.Top,
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

        return view;
    }

    private static void VerifyInitiallyHiddenAndUnowned(nint window)
    {
        if (NativeMethods.IsWindowVisible(window) ||
            NativeMethods.GetParent(window) != nint.Zero ||
            NativeMethods.GetWindow(window, NativeConstants.GetWindowOwner) != nint.Zero)
        {
            throw new InvalidOperationException(
                "The floating view must remain hidden, top-level, and unowned until validation completes.");
        }
    }

    private static void ApplyAndVerifyLayeredColorKey(nint window)
    {
        if (!NativeMethods.SetLayeredWindowAttributes(
                window,
                NativeConstants.TransparentColorKey,
                NativeConstants.FullOpacity,
                NativeConstants.LayeredWindowAttributeColorKey))
        {
            throw new Win32Exception();
        }

        if (!NativeMethods.GetLayeredWindowAttributes(
                window,
                out uint colorKey,
                out _,
                out uint flags) ||
            colorKey != NativeConstants.TransparentColorKey ||
            (flags & NativeConstants.LayeredWindowAttributeColorKey) == 0)
        {
            throw new InvalidOperationException(
                "The floating host layered color key could not be verified.");
        }
    }

    private static void PlaceAndVerifyScreenBounds(nint window, PixelRect screenBounds)
    {
        if (!NativeMethods.SetWindowPos(
                window,
                nint.Zero,
                screenBounds.Left,
                screenBounds.Top,
                screenBounds.Width,
                screenBounds.Height,
                NativeConstants.SetWindowPositionNoZOrder |
                NativeConstants.SetWindowPositionNoOwnerZOrder |
                NativeConstants.SetWindowPositionNoActivate |
                NativeConstants.SetWindowPositionFrameChanged))
        {
            throw new Win32Exception();
        }

        PixelRect actualBounds = ReadScreenBounds(window);
        if (actualBounds != screenBounds)
        {
            throw new InvalidOperationException(
                $"The floating placement was not honored. Requested={screenBounds}; Actual={actualBounds}.");
        }
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        if (window == windowHandle &&
            TryGetLayoutInvalidationReason(message, out NativeLayoutInvalidationReason reason))
        {
            Interlocked.Exchange(ref requiresLayoutRevalidation, 1);
            Interlocked.Increment(ref invalidationGeneration);
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

    private static bool TryReadSliderLayout(nint window, out SliderLayout layout)
    {
        layout = default;
        return NativeMethods.GetClientRect(window, out NativeRect client) &&
            SliderGeometry.TryCreate(client.Right - client.Left, client.Bottom - client.Top, out layout);
    }

    private static bool IsKnownStableDpi(NativeDpiAwarenessMeasurement measurement) =>
        measurement.Process != NativeDpiAwareness.Unknown &&
        measurement.Thread != NativeDpiAwareness.Unknown &&
        measurement.Window != NativeDpiAwareness.Unknown &&
        measurement.Thread == measurement.Window &&
        measurement.WindowDpi > 0;

    private static PixelRect ReadScreenBounds(nint window)
    {
        if (!NativeMethods.GetWindowRect(window, out NativeRect rectangle))
        {
            throw new Win32Exception();
        }

        return new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
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

    private static void VerifyTopLevelStyleAndOwnership(
        nint window,
        long style,
        long extendedStyle)
    {
        if (!NativeWindowStyles.MatchesFloatingWindow(style, extendedStyle))
        {
            throw new InvalidOperationException(
                "The floating host must be a non-topmost, layered, non-activating WS_POPUP tool window.");
        }

        if (NativeMethods.GetParent(window) != nint.Zero ||
            NativeMethods.GetWindow(window, NativeConstants.GetWindowOwner) != nint.Zero)
        {
            throw new InvalidOperationException("The floating host acquired a parent or owner HWND.");
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
    }

    private void ThrowIfLayoutInvalidationPending()
    {
        if (Volatile.Read(ref requiresLayoutRevalidation) != 0 ||
            !pendingLayoutInvalidations.IsEmpty)
        {
            throw new NativeLayoutInvalidatedException(
                "A display or coordinate-context invalidation is pending; the floating host must remain hidden.");
        }
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

    private void ValidateFloatingWindow()
    {
        if (windowHandle == nint.Zero || !NativeMethods.IsWindow(windowHandle))
        {
            throw new InvalidOperationException("The floating host is no longer a live HWND.");
        }

        PixelRect actualBounds = ReadScreenBounds(windowHandle);
        if (actualBounds != expectedScreenBounds)
        {
            throw new InvalidOperationException(
                $"The floating host moved outside its verified bounds. Expected={expectedScreenBounds}; Actual={actualBounds}.");
        }

        long style = ReadWindowLong(windowHandle, NativeConstants.GwlStyle);
        long extendedStyle = ReadWindowLong(windowHandle, NativeConstants.GwlExtendedStyle);
        VerifyTopLevelStyleAndOwnership(windowHandle, style, extendedStyle);

        NativeDpiAwarenessMeasurement currentDpiAwareness = NativeDpiAwarenessProbe.Measure(windowHandle);
        if (currentDpiAwareness != expectedDpiAwareness)
        {
            throw new InvalidOperationException(
                "The floating host DPI context changed after placement verification.");
        }

        if (currentDpiAwareness.WindowDpi != expectedDpi)
        {
            throw new InvalidOperationException(
                "The floating host no longer matches its verified monitor DPI.");
        }
    }

    private void DestroyCreatedWindow()
    {
        nint handle = windowHandle;
        if (handle == nint.Zero)
        {
            return;
        }

        _ = NativeMethods.ShowWindow(handle, NativeConstants.ShowWindowHide);
        if (NativeMethods.IsWindow(handle) &&
            !NativeMethods.DestroyWindow(handle) &&
            NativeMethods.IsWindow(handle))
        {
            int error = Marshal.GetLastWin32Error();
            throw error == 0
                ? new InvalidOperationException("The floating host HWND could not be destroyed.")
                : new Win32Exception(error, "The floating host HWND could not be destroyed.");
        }

        windowHandle = nint.Zero;
        expectedScreenBounds = default;
        expectedDpiAwareness = default;
        expectedDpi = 0;
        Interlocked.Exchange(ref requiresLayoutRevalidation, 0);
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
            throw new InvalidOperationException("The floating host has not been created.");
        }

        if (owningManagedThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Native window operations must run on the HWND owner thread.");
        }
    }

    private static int SignedLowWord(nint value) => unchecked((short)(value.ToInt64() & 0xFFFF));

    private static int SignedHighWord(nint value) => unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));
}
