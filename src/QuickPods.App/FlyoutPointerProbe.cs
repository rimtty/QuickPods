using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace QuickPods.App;

[SuppressMessage(
    "Interoperability",
    "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time",
    Justification = "The two blittable user32 probes avoid enabling unsafe source-generated interop for the WPF application.")]
internal static class FlyoutPointerProbe
{
    internal static bool IsPointerWithin(nint windowHandle)
    {
        if (windowHandle == nint.Zero ||
            !GetCursorPos(out NativePoint pointer) ||
            !GetWindowRect(windowHandle, out NativeRect bounds))
        {
            return false;
        }

        return pointer.X >= bounds.Left &&
            pointer.X < bounds.Right &&
            pointer.Y >= bounds.Top &&
            pointer.Y < bounds.Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetCursorPos", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out NativeRect rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }
}
