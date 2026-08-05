using System.Runtime.InteropServices;

namespace QuickPods.Spike.TaskbarHost.Interop;

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

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
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
internal struct NativePaintStruct
{
    internal nint DeviceContext;
    [MarshalAs(UnmanagedType.Bool)]
    internal bool Erase;
    internal NativeRect PaintRectangle;
    [MarshalAs(UnmanagedType.Bool)]
    internal bool Restore;
    [MarshalAs(UnmanagedType.Bool)]
    internal bool IncUpdate;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    internal byte[]? Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeGdiPoint
{
    internal NativeGdiPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    internal int X;
    internal int Y;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint NativeWindowProcedure(nint window, uint message, nuint wParam, nint lParam);
