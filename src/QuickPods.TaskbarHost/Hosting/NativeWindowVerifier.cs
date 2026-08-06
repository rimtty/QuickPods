using System.ComponentModel;
using System.Runtime.InteropServices;
using QuickPods.TaskbarHost.Geometry;
using QuickPods.TaskbarHost.Interop;
using NativeRect = QuickPods.TaskbarHost.Interop.TaskbarNativeMethods.NativeRect;

namespace QuickPods.TaskbarHost.Hosting;

internal static class NativeWindowVerifier
{
    private const int ClassNameCapacity = 128;

    internal static void VerifyIdentity(nint window, string expectedClassName)
    {
        uint threadId = TaskbarNativeMethods.GetWindowThreadProcessId(window, out uint processId);
        if (threadId == 0 || processId != (uint)Environment.ProcessId)
        {
            throw new InvalidOperationException("The native view HWND is not owned by this process.");
        }

        char[] buffer = new char[ClassNameCapacity];
        int length = HostNativeMethods.GetClassName(window, buffer, buffer.Length);
        if (length <= 0 ||
            !string.Equals(
                new string(buffer, 0, length),
                expectedClassName,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The native view HWND class is not trusted.");
        }
    }

    internal static uint ReadWindowLong(nint window, int index)
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

    internal static PixelRect ReadScreenBounds(nint window)
    {
        if (!TaskbarNativeMethods.GetWindowRect(window, out NativeRect rectangle))
        {
            throw new Win32Exception();
        }

        return rectangle.ToPixelRect();
    }

    internal static bool IsUncloaked(nint window) =>
        TaskbarNativeMethods.DwmGetWindowAttribute(
            window,
            TaskbarNativeMethods.DwmWindowAttributeCloaked,
            out uint cloaked,
            sizeof(uint)) == 0 && cloaked == 0;

    internal static void ApplyAndVerifyColorKey(nint window)
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
            throw new InvalidOperationException("The native view color key could not be verified.");
        }
    }

    internal static bool TryReadSliderLayout(nint window, out SliderLayout layout)
    {
        layout = default;
        return GdiNativeMethods.GetClientRect(window, out GdiNativeMethods.NativeRect client) &&
            SliderGeometry.TryCreate(
                client.Right - client.Left,
                client.Bottom - client.Top,
                out layout);
    }
}
