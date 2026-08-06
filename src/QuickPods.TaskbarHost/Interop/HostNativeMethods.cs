using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace QuickPods.TaskbarHost.Interop;

[SuppressMessage(
    "Interoperability",
    "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time",
    Justification = "Window procedures require runtime delegate marshalling and classic Win32 signatures.")]
internal static class HostNativeMethods
{
    internal const int ErrorClassAlreadyExists = 1410;
    internal const int GwlpUserData = -21;
    internal const int GwlStyle = -16;
    internal const int GwlExtendedStyle = -20;
    internal const uint GetWindowOwner = 4;

    internal const uint WmNcCreate = 0x0081;
    internal const uint WmNcDestroy = 0x0082;
    internal const uint WmPaint = 0x000F;
    internal const uint WmEraseBackground = 0x0014;
    internal const uint WmSettingChange = 0x001A;
    internal const uint WmDisplayChange = 0x007E;
    internal const uint WmMouseActivate = 0x0021;
    internal const uint WmThemeChanged = 0x031A;
    internal const uint WmMouseMove = 0x0200;
    internal const uint WmLeftButtonDown = 0x0201;
    internal const uint WmLeftButtonUp = 0x0202;
    internal const uint WmRightButtonUp = 0x0205;
    internal const uint WmMouseWheel = 0x020A;
    internal const uint WmMouseHover = 0x02A1;
    internal const uint WmMouseLeave = 0x02A3;
    internal const uint WmCaptureChanged = 0x0215;
    internal const uint WmDpiChanged = 0x02E0;
    internal const uint WmDpiChangedBeforeParent = 0x02E2;
    internal const uint WmDpiChangedAfterParent = 0x02E3;
    internal const uint WmQuit = 0x0012;

    internal const nint MouseActivateNoActivate = 3;
    internal const int ShowWindowHide = 0;
    internal const int ShowWindowNoActivate = 8;
    internal const uint LayeredWindowAttributeColorKey = 0x00000001;
    internal const byte FullOpacity = 255;
    internal const uint SetWindowPositionNoSize = 0x0001;
    internal const uint SetWindowPositionNoMove = 0x0002;
    internal const uint SetWindowPositionNoZOrder = 0x0004;
    internal const uint SetWindowPositionNoActivate = 0x0010;
    internal const uint SetWindowPositionFrameChanged = 0x0020;
    internal const uint SetWindowPositionShowWindow = 0x0040;
    internal const uint SetWindowPositionNoOwnerZOrder = 0x0200;
    internal const nint WindowInsertAfterTop = 0;
    internal const uint PeekMessageRemove = 0x0001;
    internal const uint SpiGetHighContrast = 0x0042;
    internal const uint HighContrastOn = 0x00000001;
    internal const uint TrackMouseEventHover = 0x00000001;
    internal const uint TrackMouseEventLeave = 0x00000002;
    internal const uint FlyoutHoverTimeMilliseconds = 180;
    internal const int ColorWindow = 5;
    internal const int ColorWindowText = 8;
    internal const int ColorHighlight = 13;
    internal const int ColorGrayText = 17;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint NativeWindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", EntryPoint = "SetLastError", ExactSpelling = true)]
    internal static extern void SetLastError(uint errorCode);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClass(ref NativeWindowClass windowClass);

    [DllImport("user32.dll", EntryPoint = "GetClassInfoExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClassInfo(
        nint instance,
        string className,
        ref NativeWindowClassInfo windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
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

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string messageName);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "ShowWindow", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", EntryPoint = "SetParent", ExactSpelling = true, SetLastError = true)]
    internal static extern nint SetParent(nint child, nint newParent);

    [DllImport("user32.dll", EntryPoint = "GetParent", ExactSpelling = true)]
    internal static extern nint GetParent(nint child);

    [DllImport("user32.dll", EntryPoint = "GetWindow", ExactSpelling = true)]
    internal static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(nint window, [Out] char[] className, int capacity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetWindowLongPointer(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint SetWindowLongPointer(nint window, int index, nint newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPosition(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "MapWindowPoints", ExactSpelling = true, SetLastError = true)]
    internal static extern int MapWindowPoints(nint from, nint to, ref NativePoint points, uint pointCount);

    [DllImport("user32.dll", EntryPoint = "SetLayeredWindowAttributes", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetLayeredWindowAttributes", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLayeredWindowAttributes(
        nint window,
        out uint colorKey,
        out byte alpha,
        out uint flags);

    [DllImport("user32.dll", EntryPoint = "InvalidateRect", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRect(
        nint window,
        nint rectangle,
        [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("user32.dll", EntryPoint = "UpdateWindow", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "SetCapture", ExactSpelling = true)]
    internal static extern nint SetCapture(nint window);

    [DllImport("user32.dll", EntryPoint = "GetCapture", ExactSpelling = true)]
    internal static extern nint GetCapture();

    [DllImport("user32.dll", EntryPoint = "ReleaseCapture", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "TrackMouseEvent", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TrackMouseEvent(ref NativeTrackMouseEvent tracking);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(
        out NativeMessage message,
        nint window,
        uint minimum,
        uint maximum,
        uint removal);

    [DllImport("user32.dll", EntryPoint = "TranslateMessage", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW", CharSet = CharSet.Unicode)]
    internal static extern nint DispatchMessage(ref NativeMessage message);

    [DllImport("user32.dll", EntryPoint = "PostQuitMessage", ExactSpelling = true)]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    internal static extern nint DefWindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref NativeHighContrast value,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetSysColor", ExactSpelling = true)]
    internal static extern uint GetSystemColor(int index);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NativeWindowClass
    {
        internal uint Size;
        internal uint Style;
        internal nint WindowProcedure;
        internal int ClassExtraBytes;
        internal int WindowExtraBytes;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint BackgroundBrush;
        internal string? MenuName;
        internal string ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeWindowClassInfo
    {
        internal uint Size;
        internal uint Style;
        internal nint WindowProcedure;
        internal int ClassExtraBytes;
        internal int WindowExtraBytes;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint BackgroundBrush;
        internal nint MenuName;
        internal nint ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeCreateStruct
    {
        internal nint CreateParameters;
        internal nint Instance;
        internal nint Menu;
        internal nint Parent;
        internal int Height;
        internal int Width;
        internal int Y;
        internal int X;
        internal int Style;
        internal nint Name;
        internal nint ClassName;
        internal uint ExtendedStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMessage
    {
        internal nint Window;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal NativePoint Point;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeTrackMouseEvent
    {
        internal uint Size;
        internal uint Flags;
        internal nint TrackWindow;
        internal uint HoverTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NativeHighContrast
    {
        internal uint Size;
        internal uint Flags;
        internal nint DefaultScheme;
    }
}
