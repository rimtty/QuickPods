using System.Runtime.InteropServices;

namespace QuickPods.TaskbarObserver;

internal static class ObserverNativeMethods
{
    private const string PrimaryTaskbarClassName = "Shell_TrayWnd";

    internal static PrimaryTaskbarIdentity FindPrimaryTaskbar()
    {
        var matches = new List<PrimaryTaskbarIdentity>();
        bool enumerated = EnumWindows(
            (window, _) =>
            {
                char[] className = new char[256];
                int classNameLength = GetClassName(window, className, className.Length);
                if (classNameLength > 0 &&
                    string.Equals(
                        new string(className, 0, classNameLength),
                        PrimaryTaskbarClassName,
                        StringComparison.Ordinal) &&
                    IsWindowVisible(window) &&
                    GetWindowThreadProcessId(window, out uint processId) != 0 &&
                    processId != 0)
                {
                    matches.Add(new(window, processId));
                }

                return true;
            },
            nint.Zero);
        if (!enumerated || matches.Count != 1)
        {
            throw new InvalidOperationException("The primary taskbar generation is unavailable or ambiguous.");
        }

        return matches[0];
    }

    internal readonly record struct PrimaryTaskbarIdentity(nint WindowHandle, uint ExplorerProcessId);

    private delegate bool EnumWindowsProcedure(nint windowHandle, nint parameter);

    [DllImport("user32.dll", EntryPoint = "EnumWindows", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProcedure callback, nint parameter);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        nint windowHandle,
        [Out] char[] className,
        int maximumCount);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
}
