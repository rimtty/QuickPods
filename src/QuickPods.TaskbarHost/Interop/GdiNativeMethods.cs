using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QuickPods.TaskbarHost.Interop;

[SuppressMessage(
    "Interoperability",
    "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time",
    Justification = "The paint structures and safe-handle ownership are clearest with classic Win32 signatures.")]
internal static class GdiNativeMethods
{
    internal const int PenStyleSolid = 0;
    internal const uint RasterOperationSourceCopy = 0x00CC0020;

    [DllImport("user32.dll", EntryPoint = "BeginPaint", ExactSpelling = true)]
    internal static extern nint BeginPaint(nint windowHandle, out NativePaintStruct paint);

    [DllImport("user32.dll", EntryPoint = "EndPaint", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndPaint(nint windowHandle, ref NativePaintStruct paint);

    [DllImport("user32.dll", EntryPoint = "GetClientRect", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll", EntryPoint = "FillRect", ExactSpelling = true)]
    internal static extern int FillRect(nint deviceContext, ref NativeRect rectangle, nint brush);

    [DllImport("gdi32.dll", EntryPoint = "CreateSolidBrush", ExactSpelling = true, SetLastError = true)]
    internal static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll", EntryPoint = "CreatePen", ExactSpelling = true, SetLastError = true)]
    internal static extern nint CreatePen(int style, int width, uint color);

    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC", ExactSpelling = true, SetLastError = true)]
    internal static extern nint CreateCompatibleDeviceContext(nint deviceContext);

    [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap", ExactSpelling = true, SetLastError = true)]
    internal static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

    [DllImport("gdi32.dll", EntryPoint = "DeleteDC", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDeviceContext(nint deviceContext);

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint graphicsObject);

    [DllImport("gdi32.dll", EntryPoint = "SelectObject", ExactSpelling = true)]
    internal static extern nint SelectObject(nint deviceContext, nint graphicsObject);

    [DllImport("gdi32.dll", EntryPoint = "BitBlt", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(
        nint destinationDeviceContext,
        int destinationX,
        int destinationY,
        int width,
        int height,
        nint sourceDeviceContext,
        int sourceX,
        int sourceY,
        uint rasterOperation);

    [DllImport("gdi32.dll", EntryPoint = "RoundRect", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RoundRect(
        nint deviceContext,
        int left,
        int top,
        int right,
        int bottom,
        int ellipseWidth,
        int ellipseHeight);

    [DllImport("gdi32.dll", EntryPoint = "Ellipse", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Ellipse(nint deviceContext, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", EntryPoint = "Polygon", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Polygon(nint deviceContext, [In] NativeGdiPoint[] points, int count);

    [DllImport("gdi32.dll", EntryPoint = "Arc", ExactSpelling = true)]
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

    [DllImport("gdi32.dll", EntryPoint = "MoveToEx", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveTo(nint deviceContext, int x, int y, nint previousPoint);

    [DllImport("gdi32.dll", EntryPoint = "LineTo", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LineTo(nint deviceContext, int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
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
    internal readonly struct NativeGdiPoint
    {
        internal NativeGdiPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        internal readonly int X;
        internal readonly int Y;
    }
}

internal sealed class SafeGdiObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeGdiObjectHandle()
        : base(true)
    {
    }

    internal static SafeGdiObjectHandle FromOwnedHandle(nint handle)
    {
        var safeHandle = new SafeGdiObjectHandle();
        safeHandle.SetHandle(handle);
        return safeHandle;
    }

    protected override bool ReleaseHandle() => GdiNativeMethods.DeleteObject(handle);
}

internal sealed class SafeMemoryDeviceContextHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeMemoryDeviceContextHandle()
        : base(true)
    {
    }

    internal static SafeMemoryDeviceContextHandle FromOwnedHandle(nint handle)
    {
        var safeHandle = new SafeMemoryDeviceContextHandle();
        safeHandle.SetHandle(handle);
        return safeHandle;
    }

    protected override bool ReleaseHandle() => GdiNativeMethods.DeleteDeviceContext(handle);
}
