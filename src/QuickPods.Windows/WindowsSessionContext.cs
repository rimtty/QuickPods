using System.Runtime.InteropServices;

namespace QuickPods.Windows;

internal static partial class WindowsSessionContext
{
    private const int SystemMetricRemoteSession = 0x1000;

    internal static bool IsRemoteSession => NativeMethods.GetSystemMetrics(SystemMetricRemoteSession) != 0;

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial int GetSystemMetrics(int index);
    }
}
