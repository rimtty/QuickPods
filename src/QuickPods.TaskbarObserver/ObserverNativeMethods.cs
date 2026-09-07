using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QuickPods.TaskbarObserver;

[SuppressMessage(
    "Interoperability",
    "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time",
    Justification = "Window procedures, WinEvent hooks, and window enumeration require runtime delegate marshalling and classic Win32 signatures.")]
internal static class ObserverNativeMethods
{
    private const string PrimaryTaskbarClassName = "Shell_TrayWnd";

    internal const uint WmDestroy = 0x0002;
    internal const uint WmClose = 0x0010;
    internal const uint WmQuit = 0x0012;
    internal const uint WmSettingChange = 0x001A;
    internal const uint WmDisplayChange = 0x007E;
    internal const uint WmNcCreate = 0x0081;
    internal const uint WmNcDestroy = 0x0082;
    internal const uint WmTimer = 0x0113;
    internal const uint WmThemeChanged = 0x031A;

    internal const uint WindowStylePopup = 0x80000000;
    internal const uint WindowExtendedStyleToolWindow = 0x00000080;
    internal const uint WindowExtendedStyleNoActivate = 0x08000000;
    internal const int GwlpUserData = -21;
    internal const int ErrorClassAlreadyExists = 1410;
    internal const uint PeekMessageRemove = 0x0001;

    internal const uint GetAncestorRoot = 2;
    internal const int ObjectIdWindow = 0;
    internal const int ChildIdSelf = 0;

    internal const uint EventObjectCreate = 0x8000;
    internal const uint EventObjectDestroy = 0x8001;
    internal const uint EventObjectShow = 0x8002;
    internal const uint EventObjectHide = 0x8003;
    internal const uint EventObjectReorder = 0x8004;
    internal const uint EventObjectLocationChange = 0x800B;
    internal const uint WinEventOutOfContext = 0x0000;
    internal const uint WinEventSkipOwnProcess = 0x0002;

    internal const nuint ShellHookWindowCreated = 1;
    internal const nuint ShellHookWindowDestroyed = 2;
    internal const nuint ShellHookRedraw = 6;
    internal const nuint ShellHookWindowReplaced = 13;

    internal const uint RegistryNotifyChangeName = 0x00000001;
    internal const uint RegistryNotifyChangeLastSet = 0x00000004;
    internal const uint RegistryNotifyThreadAgnostic = 0x10000000;

    internal const uint QueueStatusAllInput = 0x04FF;
    internal const uint MessageWaitInputAvailable = 0x0004;
    internal const uint WaitFailed = 0xFFFFFFFF;
    internal const uint Infinite = 0xFFFFFFFF;

    internal const nuint IdentityTimerId = 1;
    internal const nuint SettleTimerId = 2;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint NativeWindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WinEventProcedure(
        nint hook,
        uint eventId,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate bool EnumWindowsProcedure(nint windowHandle, nint parameter);

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

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", EntryPoint = "SetLastError", ExactSpelling = true)]
    internal static extern void SetLastError(uint errorCode);

    [DllImport("user32.dll", EntryPoint = "EnumWindows", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProcedure callback, nint parameter);

    [DllImport("user32.dll", EntryPoint = "EnumChildWindows", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(nint parent, EnumWindowsProcedure callback, nint parameter);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        nint windowHandle,
        [Out] char[] className,
        int maximumCount);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "IsWindow", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetAncestor", ExactSpelling = true)]
    internal static extern nint GetAncestor(nint windowHandle, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetParent", ExactSpelling = true)]
    internal static extern nint GetParent(nint windowHandle);

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

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    internal static extern nint DefWindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetWindowLongPointer(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint SetWindowLongPointer(nint window, int index, nint newValue);

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

    [DllImport("user32.dll", EntryPoint = "PostMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "PostQuitMessage", ExactSpelling = true)]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", EntryPoint = "MsgWaitForMultipleObjectsEx", ExactSpelling = true, SetLastError = true)]
    internal static extern uint MessageWaitForMultipleObjects(
        uint count,
        nint[] handles,
        uint milliseconds,
        uint wakeMask,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string messageName);

    [DllImport("user32.dll", EntryPoint = "RegisterShellHookWindow", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterShellHookWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "DeregisterShellHookWindow", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeregisterShellHookWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "SetWinEventHook", ExactSpelling = true, SetLastError = true)]
    internal static extern nint SetWinEventHook(
        uint minimumEvent,
        uint maximumEvent,
        nint module,
        WinEventProcedure callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "UnhookWinEvent", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", EntryPoint = "SetTimer", ExactSpelling = true, SetLastError = true)]
    internal static extern nuint SetTimer(nint window, nuint timerId, uint elapseMilliseconds, nint callback);

    [DllImport("user32.dll", EntryPoint = "KillTimer", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool KillTimer(nint window, nuint timerId);

    [DllImport("advapi32.dll", EntryPoint = "RegNotifyChangeKeyValue", ExactSpelling = true)]
    internal static extern int RegistryNotifyChangeKeyValue(
        SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint notifyFilter,
        SafeWaitHandle eventHandle,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
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
}
