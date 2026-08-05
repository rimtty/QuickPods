using System.Runtime.InteropServices;

namespace QuickPods.Spike.TaskbarHost.Interop;

#pragma warning disable SYSLIB1054 // Struct and callback marshalling are clearer with the classic Win32 signatures here.
internal static class NativeMethods
{
    [DllImport("kernel32.dll", ExactSpelling = true)]
    internal static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW", SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    internal static extern void SetLastError(uint errorCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static extern ushort RegisterClass(ref NativeWindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    internal static extern nint CreateWindow(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    internal static extern nint SetParent(nint child, nint newParent);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetParent(nint child);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    internal static extern nint DefWindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW", SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string messageName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPointer(nint window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPointer(nint window, int index, nint newValue);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    internal static extern int MapWindowPoints(nint from, nint to, ref NativePoint points, uint pointCount);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLayeredWindowAttributes(
        nint window,
        out uint colorKey,
        out byte alpha,
        out uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRect(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    internal static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint BeginPaint(nint window, out NativePaintStruct paint);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndPaint(nint window, ref NativePaintStruct paint);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int FillRect(nint deviceContext, ref NativeRect rectangle, nint brush);

    [DllImport("user32.dll", EntryPoint = "GetDC", ExactSpelling = true)]
    internal static extern nint GetDeviceContext(nint window);

    [DllImport("user32.dll", EntryPoint = "ReleaseDC", ExactSpelling = true)]
    internal static extern int ReleaseDeviceContext(nint window, nint deviceContext);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint SetCapture(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetCapture();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(out NativeMessage message, nint window, uint minimum, uint maximum, uint removal);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    internal static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetThreadDpiAwarenessContext();

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetWindowDpiAwarenessContext(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int GetAwarenessFromDpiAwarenessContext(nint value);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern uint GetDpiForWindow(nint window);

    [DllImport("shcore.dll", ExactSpelling = true)]
    internal static extern int GetProcessDpiAwareness(nint process, out int awareness);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    internal static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    internal static extern nint CreatePen(int style, int width, uint color);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint graphicsObject);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    internal static extern nint SelectObject(nint deviceContext, nint graphicsObject);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    internal static extern uint GetPixel(nint deviceContext, int x, int y);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RoundRect(
        nint deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Ellipse(nint deviceContext, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Polygon(nint deviceContext, [In] NativeGdiPoint[] points, int count);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Arc(
        nint deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int startX,
        int startY,
        int endX,
        int endY);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern uint GetGuiResources(nint process, int flags);
}
#pragma warning restore SYSLIB1054
